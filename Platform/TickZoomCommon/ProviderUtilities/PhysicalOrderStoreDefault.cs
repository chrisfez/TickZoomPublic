﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.Common
{
    public class PhysicalOrderStoreDefault : PhysicalOrderStore, LogAware
    {
        private Log log;
        private volatile bool info;
        private volatile bool trace;
        private volatile bool debug;
        private volatile bool anySnapShotWritten = false;
        public void RefreshLogLevel()
        {
            if (log != null)
            {
                info = log.IsDebugEnabled;
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }

        private class SymbolPosition
        {
            public long Position;
        }

        private Dictionary<int, CreateOrChangeOrder> ordersBySequence = new Dictionary<int, CreateOrChangeOrder>();
        private Dictionary<string, CreateOrChangeOrder> ordersByBrokerId = new Dictionary<string, CreateOrChangeOrder>();
        private Dictionary<long, CreateOrChangeOrder> ordersBySerial = new Dictionary<long, CreateOrChangeOrder>();
        private Dictionary<long, SymbolPosition> positions = new Dictionary<long, SymbolPosition>();
        private Dictionary<int, StrategyPosition> strategyPositions = new Dictionary<int, StrategyPosition>();
        private TaskLock ordersLocker = new TaskLock();
        private string databasePath;
        private FileStream fs;
        private MemoryStream memory = null;
        private BinaryWriter writer = null;
        private BinaryReader reader = null;
        private Dictionary<CreateOrChangeOrder, int> unique = new Dictionary<CreateOrChangeOrder, int>();
        private Dictionary<int,CreateOrChangeOrder> uniqueIds = new Dictionary<int,CreateOrChangeOrder>();
        private Dictionary<int,int> replaceIds = new Dictionary<int,int>();
        private Dictionary<int, int> originalIds = new Dictionary<int, int>();
        private TimeStamp lastSequenceReset;
        private int uniqueId = 0;
        private long snapshotTimer;
        private Action writeFileAction;
        private IAsyncResult writeFileResult;
        private long snapshotLength = 0;
        private int updateCount = 0;
        private long snapshotRolloverSize = 128*1024;
        private string storeName;
        private string dbFolder;
        private TaskLock snapshotLocker = new TaskLock();
        private int remoteSequence = 0;
        private int localSequence = 0;
        private object fileLocker = new object();
        private static int lockCounter = 0;

        public PhysicalOrderStoreDefault(string name)
        {
            log = Factory.SysLog.GetLogger(typeof(PhysicalOrderStoreDefault));
            log.Register(this);
            storeName = name;
            writeFileAction = SnapShotHandler;
            var appData = Factory.Settings["AppDataFolder"];
            dbFolder = Path.Combine(appData, "DataBase");
            Directory.CreateDirectory(dbFolder);
            databasePath = Path.Combine(dbFolder, name + ".dat");
            memory = new MemoryStream();
            writer = new BinaryWriter(memory, Encoding.UTF8);
            reader = new BinaryReader(memory, Encoding.UTF8);
        }

        public bool TryOpen()
        {
            if( fs != null && fs.CanWrite) return true;
            var list = new List<Exception>();
            var errorCount = 0;
            while( errorCount < 3 && !isDisposed)
            {
                try
                {
                    fs = new FileStream(databasePath, FileMode.Append, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
                    log.Info("Opened " + databasePath);
                    snapshotLength = fs.Length;
                    return true;
                }
                catch (IOException ex)
                {
                    list.Add(ex);
                    Thread.Sleep(1000);
                    errorCount++;
                }
            }
            if( list.Count > 0)
            {
                var ex = list[list.Count - 1];
                throw new ApplicationException( "Failed to open the snapshot file after 3 tries", ex);
            }
            return false;
        }

        public PhysicalOrderLock Lock()
        {
            Monitor.Enter(snapshotLocker);
            Interlocked.Increment(ref lockCounter);
            return new PhysicalOrderLock(this);
        }

        public void Unlock()
        {
            Monitor.Exit(snapshotLocker);
            Interlocked.Decrement(ref lockCounter);
        }

        public static bool IsLocked
        {
            get { return lockCounter > 0; }
        }

        public string DatabasePath
        {
            get { return databasePath; }
        }

        public long SnapshotRolloverSize
        {
            get { return snapshotRolloverSize; }
            set { snapshotRolloverSize = value; }
        }

        private void AssertAtomic()
        {
            if (!PhysicalOrderStoreDefault.IsLocked)
            {
                log.Error("Attempt to modify PhysicalOrder w/o locking PhysicalOrderStore first.\n" + Environment.StackTrace);
            }
        }

        public int RemoteSequence
        {
            get { return remoteSequence; }
        }

        public int LocalSequence
        {
            get { return localSequence; }
        }

        public TimeStamp LastSequenceReset
        {
            get { return lastSequenceReset; }
            set { lastSequenceReset = value; }
        }

        private bool AddUniqueOrder(CreateOrChangeOrder order)
        {
            AssertAtomic();
            int id;
            if( !unique.TryGetValue(order, out id))
            {
                unique.Add(order,++uniqueId);
                return true;
            }
            return false;
        }

        public void TrySnapshot()
        {
            if (isDisposed) return;
            if (updateCount > 100)
            {
                ForceSnapShot();
            }
        }

        public void ForceSnapShot()
        {
            using( snapshotLocker.Using())
            {
                if (writeFileResult != null)
                {
                    if (writeFileResult.IsCompleted)
                    {
                        writeFileAction.EndInvoke(writeFileResult);
                        writeFileResult = null;
                    }
                }
                if( writeFileResult == null)
                {
                    writeFileResult = writeFileAction.BeginInvoke(null, null);
                    updateCount = 0;
                    snapshotTimer = Factory.TickCount;
                }
            }
        }

        public void WaitForSnapshot()
        {
          
            while (writeFileResult != null && !writeFileResult.IsCompleted)
            {
                Thread.Sleep(100);
            }
            lock(snapshotLocker.Using())
            {
                if (writeFileResult != null && writeFileResult.IsCompleted)
                {
                    writeFileAction.EndInvoke(writeFileResult);
                    writeFileResult = null;
                }
            }
        }

        public struct SnapshotFile
        {
            public int Order;
            public string Filename;
        }

        private IList<SnapshotFile> FindSnapshotFiles()
        {
            var files = Directory.GetFiles(dbFolder, storeName + ".dat.*", SearchOption.TopDirectoryOnly);
            var fileList = new List<SnapshotFile>();
            foreach (var file in files)
            {
                var parts = file.Split('.');
                if (parts.Length == 3)
                {
                    int count;
                    if (int.TryParse(parts[2], out count) && count > 0)
                    {
                        fileList.Add(new SnapshotFile { Order = count, Filename = file });
                    }
                }
                else
                {
                    fileList.Add(new SnapshotFile { Order = 0, Filename = file });
                }
            }
            fileList.Sort((a,b) => a.Order - b.Order);
            return fileList;
        }

        private void ForceSnapshotRollover()
        {
            if( debug) log.Debug("Creating new snapshot file and rolling older ones to higher number.");
            lock( fileLocker)
            {
                TryClose();
                var files = FindSnapshotFiles();
                for (var i = files.Count - 1; i >= 0; i--)
                {
                    var count = files[i].Order;
                    var source = files[i].Filename;
                    if (File.Exists(source))
                    {
                        if (count > 9)
                        {
                            File.Delete(source);
                        }
                        else
                        {
                            var replace = Path.Combine(dbFolder, storeName + ".dat." + (count + 1));
                            var errorCount = 0;
                            var errorList = new List<Exception>();
                            while (errorCount < 3)
                            {
                                try
                                {
                                    File.Move(source, replace);
                                    break;
                                }
                                catch (IOException ex)
                                {
                                    errorList.Add(ex);
                                    errorCount++;
                                    Thread.Sleep(1000);
                                }
                            }
                            if (errorList.Count > 0)
                            {
                                var ex = errorList[errorList.Count - 1];
                                System.Diagnostics.Debugger.Break();
                                throw new ApplicationException("Failed to mov " + source + " to " + replace, ex);
                            }
                        }
                    }
                }
            }
        }

        private void CheckSnapshotRollover()
        {
            if (snapshotLength >= SnapshotRolloverSize || forceSnapShotRollover)
            {
                forceSnapShotRollover = false;
                log.Info("Snapshot length greater than snapshot rollover: " + SnapshotRolloverSize);
                ForceSnapshotRollover();
            }
        }

        private IEnumerable<CreateOrChangeOrder> OrderReferences(CreateOrChangeOrder order)
        {
            if( order.ReplacedBy != null)
            {
                if( AddUniqueOrder(order.ReplacedBy))
                {
                    yield return order.ReplacedBy;
                    foreach (var sub in OrderReferences(order.ReplacedBy))
                    {
                        if (AddUniqueOrder(sub))
                        {
                            yield return sub;
                        }
                    }
                }
            }
            if( order.OriginalOrder != null)
            {
                if( AddUniqueOrder(order.OriginalOrder))
                {
                    yield return order.OriginalOrder;
                    foreach (var sub in OrderReferences(order.OriginalOrder))
                    {
                        if (AddUniqueOrder(sub))
                        {
                            yield return sub;
                        }
                    }
                }
            }
        }

        public Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol)
        {
            AssertAtomic();
            var result = new ActiveList<CreateOrChangeOrder>();
            var list = GetOrders((o) => o.Symbol == symbol);
            foreach (var order in list)
            {
                if (order.OrderState != OrderState.Filled)
                {
                    result.AddLast(order);
                }
            }
            return result;
        }

        private void SnapShotHandler()
        {
            try
            {
                if( debug) log.Debug("SnapshotHandler()");
                using( Lock()) {
                    SnapShot();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex.Message, ex);
            }
        }

        private void SnapShot()
        {
            anySnapShotWritten = true;
            CheckSnapshotRollover();

            memory.SetLength(0);
            uniqueId = 0;
            unique.Clear();

            using (ordersLocker.Using())
            {
                // Save space for length.
                writer.Write((int)memory.Length);
                // Write the current sequence number
                writer.Write(remoteSequence);
                writer.Write(LocalSequence);
                writer.Write(lastSequenceReset.Internal);
                if (debug) log.Debug("Snapshot writing Local Sequence  " + localSequence + ", Remote Sequence " + remoteSequence);
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    AddUniqueOrder(order);
                    if( trace) log.Trace("Snapshot found order by Id: " + order);
                    foreach( var reference in OrderReferences(order))
                    {
                        AddUniqueOrder(reference);
                    }
                }

                foreach (var kvp in ordersBySerial)
                {
                    var order = kvp.Value;
                    AddUniqueOrder(order);
                    if (trace) log.Trace("Snapshot found order by serial: " + order);
                    foreach (var reference in OrderReferences(order))
                    {
                        AddUniqueOrder(reference);
                    }
                }

                writer.Write(unique.Count);
                foreach (var kvp in unique)
                {
                    var order = kvp.Key;
                    if (trace) log.Trace("Snapshot writing unique order: " + order);
                    var id = kvp.Value;
                    writer.Write(id);
                    writer.Write((int)order.Action);
                    writer.Write(order.BrokerOrder);
                    writer.Write(order.LogicalOrderId);
                    writer.Write(order.LogicalSerialNumber);
                    writer.Write((int)order.OrderState);
                    writer.Write(order.Price);
                    writer.Write((int)order.OrderFlags);
                    if (order.ReplacedBy != null)
                    {
                        try
                        {
                            writer.Write(unique[order.ReplacedBy]);
                        } catch( KeyNotFoundException ex)
                        {
                            var sb = new StringBuilder();
                            foreach( var kvp2 in unique)
                            {
                                var temp = kvp2.Value;
                                var temp2 = kvp2.Key;
                                sb.AppendLine(temp.ToString() + ": " + temp2.ToString());
                            }
                            throw new ApplicationException("Can't find " + order.ReplacedBy + "\n" + sb, ex);
                        }
                    }
                    else
                    {
                        writer.Write((int)0);
                    }
                    if (order.OriginalOrder != null)
                    {
                        try
                        {
                            writer.Write(unique[order.OriginalOrder]);
                        }
                        catch (KeyNotFoundException ex)
                        {
                            throw new ApplicationException("Can't find " + order.ReplacedBy, ex);
                        }
                    }
                    else
                    {
                        writer.Write((int)0);
                    }
                    writer.Write((int)order.Side);
                    writer.Write((int)order.Size);
                    writer.Write(order.Symbol.Symbol);
                    if (order.Tag == null)
                    {
                        writer.Write("");
                    }
                    else
                    {
                        writer.Write(order.Tag);
                    }
                    writer.Write((int)order.Type);
                    writer.Write(order.UtcCreateTime.Internal);
                    writer.Write(order.LastStateChange.Internal);
                    writer.Write(order.Sequence);
                }

                writer.Write(ordersBySerial.Count);
                foreach (var kvp in ordersBySerial)
                {
                    var serial = kvp.Key;
                    var order = kvp.Value;
                    writer.Write(serial);
                    writer.Write(unique[order]);
                }

                using( positionsLocker.Using())
                {
                    if (debug) log.Debug("Symbol Positions\n" + SymbolPositionsToStringInternal());
                    if (debug) log.Debug("Strategy Positions\n" + StrategyPositionsToStringInternal());

                    var positionCount = 0;
                    foreach (var kvp in positions)
                    {
                        var symbolPosition = kvp.Value;
                        if (symbolPosition.Position != 0)
                        {
                            positionCount++;
                        }
                    }
                    writer.Write(positionCount);


                    foreach (var kvp in positions)
                    {
                        var symbolPosition = kvp.Value;
                        if (symbolPosition.Position != 0)
                        {
                            positionCount++;
                            var symbol = Factory.Symbol.LookupSymbol(kvp.Key);
                            writer.Write(symbol.Symbol);
                            writer.Write(kvp.Value.Position);
                        }
                    }

                    positionCount = 0;
                    foreach (var kvp in strategyPositions)
                    {
                        var strategyPosition = kvp.Value;
                        if (strategyPosition.ExpectedPosition != 0)
                        {
                            positionCount++;
                        }
                    }

                    writer.Write(positionCount);
                    foreach (var kvp in strategyPositions)
                    {
                        var strategyPosition = kvp.Value;
                        if (strategyPosition.ExpectedPosition != 0)
                        {
                            writer.Write(strategyPosition.Symbol.ToString());
                            writer.Write(strategyPosition.Id);
                            writer.Write(strategyPosition.Recency);
                            writer.Write(strategyPosition.ExpectedPosition);
                        }
                    }
                }
            }
            memory.Position = 0;
            writer.Write((Int32)memory.Length - sizeof(Int32)); // length excludes the size of the length value.
            if( TryOpen())
            {
                fs.Write(memory.GetBuffer(), 0, (int)memory.Length);
                snapshotLength += memory.Length;
                log.Info("Wrote snapshot. Sequence Remote = " + remoteSequence + ", Local = " + localSequence +
                         ", Size = " + memory.Length + ". File Size = " + snapshotLength);
            }
            if (isDisposed)
            {
                TryClose();
            }
        }

        private void SnapshotReadAll(string filePath)
        {
            using (var readFS = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var count = 0;
                memory.SetLength(snapshotRolloverSize<<2);
                memory.Position = 0;
                do
                {
                    count = readFS.Read(memory.GetBuffer(), (int)memory.Position, (int)(memory.Length - count));
                    memory.Position += count;
                } while (count > 0);
                memory.SetLength(memory.Position);
            }
        }

        public struct Snapshot
        {
            public int Offset;
            public int Length;
        }

        private IList<Snapshot> SnapshotScan()
        {
            var snapshots = new List<Snapshot>();
            memory.Position = 0;
            while (memory.Position < memory.Length)
            {
                var snapshot = new Snapshot {Offset = (int) memory.Position, Length = reader.ReadInt32()};
                if (snapshot.Length <= 0 || memory.Position + snapshot.Length > memory.Length)
                {
                    log.Warn("Invalid snapshot length: " + snapshot.Length + ". Probably corrupt snapshot. Ignoring remainder of current snapshot file.");
                    break;
                }
                snapshots.Add(snapshot);
                memory.Position += snapshot.Length;
            }
            return snapshots;
        }

        private void TryClose()
        {
            if (fs != null)
            {
                fs.Close();
                log.Info("Closed " + databasePath);
            }
        }

        public bool Recover()
        {
            lock( fileLocker)
            {
                TryClose();
                var files = FindSnapshotFiles();
                var loaded = false;
                foreach (var file in files)
                {
                    if (debug) log.Debug("Attempting recovery from snapshot file: " + file.Filename);
                    SnapshotReadAll(file.Filename);
                    var snapshots = SnapshotScan();
                    for (var i = snapshots.Count - 1; i >= 0; i--)
                    {
                        var snapshot = snapshots[i];
                        if (debug) log.Debug("Trying snapshot at offset: " + snapshot.Offset + ", length: " + snapshot.Length);
                        if (SnapshotLoadLast(snapshot))
                        {
                            if (debug) log.Debug("Snapshot successfully loaded.");
                            loaded = true;
                            break;
                        }
                    }
                    if (loaded)
                    {
                        break;
                    }
                }
                if (loaded)
                {
                    forceSnapShotRollover = true;
                    ForceSnapShot();
                }
                return loaded;
            }
        }

        private bool forceSnapShotRollover = false;

        private bool SnapshotLoadLast(Snapshot snapshot) {

            try
            {
                uniqueIds.Clear();
                replaceIds.Clear();
                originalIds.Clear();

                memory.Position = snapshot.Offset + sizeof(Int32); // Skip the snapshot length;

                remoteSequence = reader.ReadInt32();
                localSequence = reader.ReadInt32();
                lastSequenceReset = new TimeStamp(reader.ReadInt64());

                int orderCount = reader.ReadInt32();
                for (var i = 0; i < orderCount; i++)
                {

                    var id = reader.ReadInt32();
                    var action = (OrderAction) reader.ReadInt32();
                    var brokerOrder = reader.ReadString();
                    var logicalOrderId = reader.ReadInt32();
                    var logicalSerialNumber = reader.ReadInt64();
                    var orderState = (OrderState)reader.ReadInt32();
                    var price = reader.ReadDouble();
                    var flags = (OrderFlags) reader.ReadInt32();
                    var replaceId = reader.ReadInt32();
                    var originalId = reader.ReadInt32();
                    var side = (OrderSide)reader.ReadInt32();
                    var size = reader.ReadInt32();
                    var symbol = reader.ReadString();
                    var tag = reader.ReadString();
                    if (string.IsNullOrEmpty(tag)) tag = null;
                    var type = (OrderType)reader.ReadInt32();
                    var utcCreateTime= new TimeStamp(reader.ReadInt64());
                    var lastStateChange = new TimeStamp(reader.ReadInt64());
                    var sequence = reader.ReadInt32();
                    var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
                    var order = Factory.Utility.PhysicalOrder(action, orderState, symbolInfo, side, type, flags, price, size,
                                                              logicalOrderId, logicalSerialNumber, brokerOrder, tag, utcCreateTime);
                    order.ResetLastChange(lastStateChange);
                    order.Sequence = sequence;
                    uniqueIds.Add(id, order);
                    if (replaceId != 0)
                    {
                        replaceIds.Add(id, replaceId);
                    }
                    if( originalId != 0)
                    {
                        originalIds.Add(id, originalId);
                    }
                }

                foreach (var kvp in replaceIds)
                {
                    var orderId = kvp.Key;
                    var replaceId = kvp.Value;
                    uniqueIds[orderId].ReplacedBy = uniqueIds[replaceId];
                }

                foreach (var kvp in originalIds)
                {
                    var orderId = kvp.Key;
                    var originalId = kvp.Value;
                    uniqueIds[orderId].OriginalOrder = uniqueIds[originalId];
                }

                using (ordersLocker.Using())
                {
                    ordersByBrokerId.Clear();
                    ordersBySequence.Clear();
                    ordersBySerial.Clear();
                    foreach (var kvp in uniqueIds)
                    {
                        var order = kvp.Value;
                        ordersByBrokerId[order.BrokerOrder] = order;
                        ordersBySequence[order.Sequence] = order;
                        if( order.Action == OrderAction.Cancel && order.OriginalOrder == null)
                        {
                            throw new ApplicationException("CancelOrder w/o any original order setting: " + order);
                        }
                    }

                    var bySerialCount = reader.ReadInt32();
                    for (var i = 0; i < bySerialCount; i++)
                    {
                        var logicalSerialNum = reader.ReadInt64();
                        var orderId = reader.ReadInt32();
                        var order = uniqueIds[orderId];
                        ordersBySerial[order.LogicalSerialNumber] = order;
                    }
                }

                using( positionsLocker.Using())
                {
                    var positionCount = reader.ReadInt32();
                    positions = new Dictionary<long, SymbolPosition>();
                    for (var i = 0L; i < positionCount; i++ )
                    {
                        var symbolString = reader.ReadString();
                        var symbol = Factory.Symbol.LookupSymbol(symbolString);
                        var position = reader.ReadInt64();
                        var symbolPosition = new SymbolPosition {Position = position};
                        positions.Add(symbol.BinaryIdentifier, symbolPosition);
                    }

                    positionCount = reader.ReadInt32();
                    strategyPositions = new Dictionary<int, StrategyPosition>();
                    for (var i = 0L; i < positionCount; i++)
                    {
                        var symbolString = reader.ReadString();
                        var symbol = Factory.Symbol.LookupSymbol(symbolString);
                        var strategyId = reader.ReadInt32();
                        var recency = reader.ReadInt64();
                        var position = reader.ReadInt64();
                        var strategyPosition = new StrategyPositionDefault(strategyId, symbol);
                        strategyPosition.SetExpectedPosition(position);
                        strategyPosition.Recency = recency;
                        strategyPositions.Add(strategyId, strategyPosition);
                        if( debug) log.Debug("Loaded strategy position: " + strategyPosition);
                    }
                }
                return true;
            }
            catch( Exception ex)
            {
                log.Info("Loading snapshot at offset " + snapshot.Offset + " failed due to " + ex.Message + ". Rolling back to previous snapshot.", ex);
                return false;
            }
        }

        public void SyncPositions(Iterable<StrategyPosition> strategyPositions)
        {
            if (debug)
            {
                for (var node = strategyPositions.First; node != null; node = node.Next)
                {
                    var sp = node.Value;
                    log.Debug("Received strategy position. " + sp);
                }
            }
            for (var current = strategyPositions.First; current != null; current = current.Next)
            {
                var position = current.Value;
                StrategyPosition strategyPosition;
                if (!this.strategyPositions.TryGetValue(position.Id, out strategyPosition))
                {
                    strategyPosition = new StrategyPositionDefault(position.Id, position.Symbol);
                    this.strategyPositions.Add(position.Id, strategyPosition);
                }
                strategyPosition.TrySetPosition(position.ExpectedPosition, position.Recency);
            }
        }

        public void SetActualPosition(SymbolInfo symbol, long position)
        {
            using( positionsLocker.Using())
            {
                if( debug) log.Debug("SetActualPosition( " + symbol + " = " + position + ")");
                SymbolPosition symbolPosition;
                if (!positions.TryGetValue(symbol.BinaryIdentifier, out symbolPosition))
                {
                    symbolPosition = new SymbolPosition {Position = position};
                    positions.Add(symbol.BinaryIdentifier,symbolPosition);
                }
                else
                {
                    positions[symbol.BinaryIdentifier].Position = position;
                }
            }
        }

        public long GetActualPosition( SymbolInfo symbol)
        {
            using (positionsLocker.Using())
            {
                SymbolPosition symbolPosition;
                if (positions.TryGetValue(symbol.BinaryIdentifier, out symbolPosition))
                {
                    return symbolPosition.Position;
                }
                else
                {
                    return 0L;
                }
            }
        }

        public void SetStrategyPosition(SymbolInfo symbol, int strategyId, long position)
        {
            using (positionsLocker.Using())
            {
                StrategyPosition strategyPosition;
                if (!this.strategyPositions.TryGetValue(strategyId, out strategyPosition))
                {
                    strategyPosition = new StrategyPositionDefault(strategyId, symbol);
                    this.strategyPositions.Add(strategyId, strategyPosition);
                }
                strategyPosition.SetExpectedPosition(position);
            }
        }

        public long GetStrategyPosition(int strategyId)
        {
            using (positionsLocker.Using())
            {
                StrategyPosition strategyPosition;
                if (strategyPositions.TryGetValue(strategyId, out strategyPosition))
                {
                    return strategyPosition.ExpectedPosition;
                }
                else
                {
                    return 0L;
                }
            }
        }

        public long IncreaseActualPosition(SymbolInfo symbol, long increase)
        {
            using (positionsLocker.Using())
            {
                SymbolPosition symbolPosition;
                if (!positions.TryGetValue(symbol.BinaryIdentifier, out symbolPosition))
                {
                    symbolPosition = new SymbolPosition {Position = increase};
                    positions.Add(symbol.BinaryIdentifier,symbolPosition);
                }
                else
                {
                    symbolPosition.Position += increase;
                }
                return symbolPosition.Position;
            }
        }

        public void Clear()
        {
            if( debug) log.Debug("Clearing all orders.");
            using( ordersLocker.Using())
            {
                ordersByBrokerId.Clear();
                ordersBySequence.Clear();
                ordersBySerial.Clear();
            }
        }

        public bool TryGetOrderById(string brokerOrder, out CreateOrChangeOrder order)
        {
            AssertAtomic();
            if (brokerOrder == null)
            {
                order = null;
                return false;
            }
            using (ordersLocker.Using())
            {
                return ordersByBrokerId.TryGetValue((string) brokerOrder, out order);
            }
        }

        public bool TryGetOrderBySequence(int sequence, out CreateOrChangeOrder order)
        {
            AssertAtomic();
            if (sequence == 0)
            {
                order = null;
                return false;
            }
            using (ordersLocker.Using())
            {
                return ordersBySequence.TryGetValue(sequence, out order);
            }
        }

        public CreateOrChangeOrder GetOrderById(string brokerOrder)
        {
            AssertAtomic();
            using (ordersLocker.Using())
            {
                CreateOrChangeOrder order;
                if (!ordersByBrokerId.TryGetValue((string) brokerOrder, out order))
                {
                    throw new ApplicationException("Unable to find order for id: " + brokerOrder);
                }
                return order;
            }
        }

        public CreateOrChangeOrder RemoveOrder(string clientOrderId)
        {
            if (trace) log.Trace("RemoveOrder( " + clientOrderId + ")");
            AssertAtomic();
            if (string.IsNullOrEmpty(clientOrderId))
            {
                return null;
            }
            using (ordersLocker.Using())
            {
                //TrySnapshot();
                CreateOrChangeOrder order = null;
                if (ordersByBrokerId.TryGetValue(clientOrderId, out order))
                {
                    var result = ordersByBrokerId.Remove(clientOrderId);
                    if( result && trace) log.Trace("Removed order by broker id " + clientOrderId + ": " + order);
                    CreateOrChangeOrder orderBySerial;
                    if( ordersBySerial.TryGetValue(order.LogicalSerialNumber, out orderBySerial))
                    {
                        if( orderBySerial.BrokerOrder.Equals(clientOrderId))
                        {
                            var result2 = ordersBySerial.Remove(order.LogicalSerialNumber);
                            if( result2 && trace) log.Trace("Removed order by logical id " + order.LogicalSerialNumber + ": " + orderBySerial);
                        }
                    }
                    return order;
                }
                else
                {
                    return null;
                }
            }
        }

        public bool TryGetOrderBySerial(long logicalSerialNumber, out CreateOrChangeOrder order)
        {
            AssertAtomic();
            using (ordersLocker.Using())
            {
                return ordersBySerial.TryGetValue(logicalSerialNumber, out order);
            }
        }

        public CreateOrChangeOrder GetOrderBySerial(long logicalSerialNumber)
        {
            AssertAtomic();
            using (ordersLocker.Using())
            {
                CreateOrChangeOrder order;
                if (!ordersBySerial.TryGetValue(logicalSerialNumber, out order))
                {
                    throw new ApplicationException("Unable to find order by serial for id: " + logicalSerialNumber);
                }
                return order;
            }
        }

        public void UpdateLocalSequence(int localSequence)
        {
            AssertAtomic();
            using (ordersLocker.Using())
            {
                this.localSequence = localSequence;
                TrySnapshot();
            }
        }

        public void UpdateRemoteSequence(int remoteSequence)
        {
            AssertAtomic();
            using (ordersLocker.Using())
            {
                this.remoteSequence = remoteSequence;
                TrySnapshot();
            }
        }

        public void SetSequences(int remoteSequence, int localSequence)
        {
            AssertAtomic();
            this.remoteSequence = remoteSequence;
            this.localSequence = localSequence;
        }

        public bool HasCreateOrder(CreateOrChangeOrder order)
        {
            var createOrderQueue = GetActiveOrders(order.Symbol);
            for (var current = createOrderQueue.First; current != null; current = current.Next)
            {
                var queueOrder = current.Value;
                if (order.Action == OrderAction.Create && order.LogicalSerialNumber == queueOrder.LogicalSerialNumber)
                {
                    if (debug) log.Debug("Create ignored because order was already on create order queue: " + queueOrder);
                    return true;
                }
            }
            return false;
        }

        public bool HasCancelOrder(PhysicalOrder order)
        {
            var cancelOrderQueue = GetActiveOrders(order.Symbol);
            for (var current = cancelOrderQueue.First; current != null; current = current.Next)
            {
                var clientId = current.Value;
                if (clientId.OriginalOrder != null && order.OriginalOrder.BrokerOrder == clientId.OriginalOrder.BrokerOrder)
                {
                    if (debug) log.Debug("Cancel or Changed ignored because previous order order working for: " + order);
                    return true;
                }
            }
            return false;
        }

        public void SetOrder(CreateOrChangeOrder order)
        {
            AssertAtomic();
            using (ordersLocker.Using())
            {
                if (trace ) log.Trace("Assigning order " + order.BrokerOrder + " with " + order.LogicalSerialNumber);
                ordersByBrokerId[order.BrokerOrder] = order;
                if( order.Sequence != 0)
                {
                    ordersBySequence[order.Sequence] = order;
                }
                if( order.LogicalSerialNumber != 0)
                {
                    ordersBySerial[order.LogicalSerialNumber] = order;
                    if (order.Action == OrderAction.Cancel && order.OriginalOrder == null)
                    {
                        throw new ApplicationException("CancelOrder w/o any original order setting: " + order);
                    }
                }
                //TrySnapshot();
            }
        }

        public List<CreateOrChangeOrder> GetOrders(Func<CreateOrChangeOrder,bool> select)
        {
            AssertAtomic();
            var list = new List<CreateOrChangeOrder>();
            using (ordersLocker.Using())
            {
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    if (select(order))
                    {
                        list.Add(order);
                    }
                }
            }
            return list;
        }

        public void ResetLastChange()
        {
            AssertAtomic();
            if( debug) log.Debug("Resetting last change time for all physical orders.");
            using (ordersLocker.Using())
            {
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    order.ResetLastChange();
                }
            }
        }

        public string OrdersToString()
        {
            using (ordersLocker.Using())
            {
                var sb = new StringBuilder();
                foreach (var kvp in ordersByBrokerId)
                {
                    sb.AppendLine(kvp.Value.ToString());
                }
                return sb.ToString();
            }
        }

        protected volatile bool isDisposed = false;
        private SimpleLock positionsLocker = new SimpleLock();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                log.Info("Dispose()");
                if (disposing)
                {
                    isDisposed = true;
                    if (anySnapShotWritten)
                    {
                        ForceSnapShot();
                    }
                    TryClose();
                }
            }
        }

        public int Count()
        {
            return ordersByBrokerId.Count;
        }

        public string SymbolPositionsToString()
        {
            using (positionsLocker.Using())
            {
                return SymbolPositionsToStringInternal();
            }
        }

        public string StrategyPositionsToString()
        {
            using (positionsLocker.Using())
            {
                return StrategyPositionsToStringInternal();
            }
        }

        private string SymbolPositionsToStringInternal()
        {
            var sb = new StringBuilder();
            foreach (var kvp in positions)
            {
                var symbol = Factory.Symbol.LookupSymbol(kvp.Key);
                var position = kvp.Value;
                sb.AppendLine(symbol + " " + position);
            }
            return sb.ToString();
        }

        public string StrategyPositionsToStringInternal()
        {
            var sb = new StringBuilder();
            foreach (var kvp in strategyPositions)
            {
                var strategyPosition = kvp.Value;
                sb.AppendLine(strategyPosition.ToString());
            }
            return sb.ToString();
        }
    }
}