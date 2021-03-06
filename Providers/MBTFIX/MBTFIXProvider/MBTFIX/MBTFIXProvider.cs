#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using TickZoom.Api;
using TickZoom.FIX;

namespace TickZoom.MBTFIX
{
    [SkipDynamicLoad]
	public class MBTFIXProvider : FIXProviderSupport, PhysicalOrderHandler, LogAware
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(MBTFIXProvider));
		private readonly bool info = log.IsDebugEnabled;
        private volatile bool trace = log.IsTraceEnabled;
        private volatile bool debug = log.IsDebugEnabled;
        private volatile bool verbose = log.IsVerboseEnabled;

        private class SymbolAlgorithm
        {
            public bool IsBrokerStarted;
            public OrderAlgorithm OrderAlgorithm;
        }

        public override void RefreshLogLevel()
        {
            base.RefreshLogLevel();
            if (log != null)
            {
                verbose = log.IsVerboseEnabled;
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }
        private static long nextConnectTime = 0L;
		private readonly object orderAlgorithmsLocker = new object();
        private Dictionary<long, SymbolAlgorithm> orderAlgorithms = new Dictionary<long, SymbolAlgorithm>();
        long lastLoginTry = long.MinValue;

        public enum RecoverProgress
        {
            InProgress,
            Completed,
            None,
        }
        private string fixDestination = "MBT";

        public MBTFIXProvider(string name)
		{
            log.Register(this);
			log.Notice("Using config file name: " + name);
			if( name.Contains(".config")) {
				throw new ApplicationException("Please remove .config from config section name.");
			}
  			ConfigSection = name;
		}
		
		public override void OnDisconnect() {
            HeartbeatDelay = int.MaxValue;
		    OrderStore.RequestSnapshot();
            var message = "MBTFIXProvider disconnected. Attempting to reconnect.";
            if( SyncTicks.Enabled)
            {
                log.Notice(message);
            } else
            {
                log.Error(message);
            }
            log.Info("Logging out -- Sending EndBroker event.");
            TrySendEndBroker();
        }

		public override void OnRetry() {
		}

        private void TrySendSynchronizeBroker()
        {
            //TrySend(EventType.SynchronizeBroker);
        }

        private void TrySendStartBroker(SymbolInfo symbol)
        {
            var symbolAlgorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if( symbolAlgorithm.IsBrokerStarted)
            {
                if (debug) log.Debug("Attempted StartBroker but isBrokerStarted is " + symbolAlgorithm.IsBrokerStarted);
                return;
            }
            if( !IsRecovered)
            {
                if (debug) log.Debug("Attempted StartBroker but IsRecovered is " + IsRecovered);
                return;
            }
            TrySend(EventType.StartBroker, symbol);
            symbolAlgorithm.IsBrokerStarted = true;
        }

        private void TrySend(EventType type, SymbolInfo symbol)
        {
			lock( symbolsRequestedLocker) {
                if( debug) log.Debug("Sending " + type + " for " + symbol + " ...");
				long end = Factory.Parallel.TickCount + 5000;
				while( !receiver.OnEvent(symbol,(int)type,symbol)) {
					if( isDisposed) return;
					if( Factory.Parallel.TickCount > end) {
						throw new ApplicationException("Timeout while sending " + type);
					}
					Factory.Parallel.Yield();
				}
			}
		}

        public int ProcessOrders()
        {
            return 0;
        }
		
		private void TrySendEndBroker() {

            lock (symbolsRequestedLocker)
            {
				foreach(var kvp in symbolsRequested) {
					SymbolInfo symbol = kvp.Value;
				    var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
                    if (!algorithm.IsBrokerStarted)
                    {
                        if (debug) log.Debug("Tried to send EndBroker for " + symbol + " but broker status is already offline.");
                        continue;
                    }
                    var waitIncrease = 10;
				    var waitSeconds = waitIncrease;
				    var errorFlag = false;
					long end = Factory.Parallel.TickCount + waitSeconds * 1000;
					while( !receiver.OnEvent(symbol,(int)EventType.EndBroker,symbol)) {
						if( isDisposed) return;
						if( Factory.Parallel.TickCount > end) {
							log.Error( "Waiting over " + waitSeconds + " to send " + EventType.EndBroker + " will keep trying.");
						    waitSeconds += waitIncrease;
						    errorFlag = true;
						}
						Factory.Parallel.Yield();
					}
                    if( errorFlag)
                    {
                        log.Notice(EventType.EndBroker + " successfully sent so error was resolved.");
                    }
				    algorithm.IsBrokerStarted = false;
				}
			}
		}

        private void RequestSessionUpdate()
        {
            var mbtMsg = FixFactory.Create();
            mbtMsg.SetTradingSessionRequestId(FixFactory.Sender + "-" + mbtMsg.Sequence + "-" + TimeStamp.UtcNow);
            mbtMsg.SetTradingSessionId("TSSTATE");
            mbtMsg.SetSubscriptionRequestType(1);
            mbtMsg.AddHeader("g");
            if (debug)
            {
                log.Debug("Request Session Update: \n" + mbtMsg);
            }
            SendMessage(mbtMsg);
        }

        private void SendLogin(int localSequence)
        {
            lastLoginTry = Factory.Parallel.TickCount;

            FixFactory = new FIXFactory4_4(localSequence+1, UserName, fixDestination);
            var mbtMsg = FixFactory.Create();
            mbtMsg.SetEncryption(0);
            mbtMsg.SetHeartBeatInterval(30);
            if( localSequence == 0)
            {
                mbtMsg.ResetSequence();
            }
            mbtMsg.SetEncoding("554_H1");
            mbtMsg.SetPassword(Password);
            mbtMsg.AddHeader("A");
            if (debug)
            {
                log.Debug("Login message: \n" + mbtMsg);
            }
            SendMessage(mbtMsg);
            if (SyncTicks.Enabled)
            {
                HeartbeatDelay = int.MaxValue;
            }
            else
            {
                HeartbeatDelay = int.MaxValue;
                //HeartbeatDelay = 40;
            }
        }

        public override bool OnLogin()
        {
            if (debug) log.Debug("Login()");

            if (OrderStore.Recover())
            {
                // Reset the order algorithms
                lock( orderAlgorithmsLocker)
                {
                    var symbolIds = new List<long>();
                    foreach( var kvp in orderAlgorithms)
                    {
                        symbolIds.Add(kvp.Key);
                    }
                    orderAlgorithms.Clear();
                    foreach( var symbolId in symbolIds)
                    {
                        GetAlgorithm(symbolId);
                    }
                }
                if( SyncTicks.Enabled)
                {
                    foreach (var order in OrderStore.GetOrders((x) => x.OrderState == OrderState.Pending || x.OrderState == OrderState.PendingNew))
                    {
                        var tickSync = SyncTicks.GetTickSync(order.Symbol.BinaryIdentifier);
                        tickSync.AddPhysicalOrder(order);
                    }
                }
                if (debug) log.Debug("Recovered from snapshot Local Sequence " + OrderStore.LocalSequence + ", Remote Sequence " + OrderStore.RemoteSequence);
                if (debug) log.Debug("Recovered orders from snapshot: \n" + OrderStore.OrdersToString());
                if (debug) log.Debug("Recovered symbol positions from snapshot:\n" + OrderStore.SymbolPositionsToString());
                if (debug) log.Debug("Recovered strategy positions from snapshot:\n" + OrderStore.StrategyPositionsToString());
                RemoteSequence = OrderStore.RemoteSequence;
                SendLogin(OrderStore.LocalSequence + 500);
                OrderStore.RequestSnapshot();
            }
            else
            {
                if( debug) log.Debug("Unable to recover from snapshot. Beginning full recovery.");
                RemoteSequence = 1;
                SendLogin(0);
            }
            return true;
        }

        public override void OnLogout()
        {
            if (isDisposed)
            {
                if( debug) log.Debug("OnLogOut() already disposed.");
                return;
            }
            var mbtMsg = FixFactory.Create();
            mbtMsg.AddHeader("5");
            SendMessage(mbtMsg);
            log.Info("Logout message sent: " + mbtMsg);
            log.Info("Logging out -- Sending EndBroker event.");
            TrySendEndBroker();
        }
		
		protected override void OnStartRecovery()
		{
			if( !LogRecovery) {
				MessageFIXT1_1.IsQuietRecovery = true;
			}
		    CancelRecovered();
            TryEndRecovery();
		}

        protected override void OnFinishRecovery()
        {
        }

        public override void OnStartSymbol(SymbolInfo symbol)
        {
            if( ConnectionStatus == Status.Recovered)
            {
                var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
                algorithm.OrderAlgorithm.ProcessOrders();
                if (algorithm.OrderAlgorithm.IsPositionSynced)
                {
                    TrySendStartBroker(symbol);
                }
            }
        }

		public override void OnStopSymbol(SymbolInfo symbol)
		{
            TrySendEndBroker();
        }
	
		private void RequestPositions() {
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
            fixMsg.SetSubscriptionRequestType(1);
            fixMsg.SetAccount(AccountNumber);
			fixMsg.SetPositionRequestId(1);
			fixMsg.SetPositionRequestType(0);
			fixMsg.AddHeader("AN");
			SendMessage(fixMsg);
		}

        private TimeStamp previousHeartbeatTime = default(TimeStamp);
        private TimeStamp recentHeartbeatTime = default(TimeStamp);
        private void SendHeartbeat()
        {
            if (debug) log.Debug("SendHeartBeat Status " + ConnectionStatus + ", Session Status Online " + isOrderServerOnline + ", Resend Complete " + IsResendComplete);
            if (!isOrderServerOnline) RequestSessionUpdate();
            if (!IsRecovered) TryEndRecovery();
            if (IsRecovered)
            {
                lock( orderAlgorithmsLocker)
                {
                    foreach( var kvp in orderAlgorithms)
                    {
                        var algo = kvp.Value;
                        algo.OrderAlgorithm.CheckForPending();
                    }
                }
            }
            var fixMsg = (FIXMessage4_4)FixFactory.Create();
			fixMsg.AddHeader("0");
			SendMessage( fixMsg);
            previousHeartbeatTime = recentHeartbeatTime;
            recentHeartbeatTime = TimeStamp.UtcNow;
        }

        private unsafe bool VerifyLoginAck(MessageFIXT1_1 message)
		{
		    var packetFIX = message;
		    if ("A" == packetFIX.MessageType &&
		        "FIX.4.4" == packetFIX.Version &&
		        "MBT" == packetFIX.Sender &&
		        UserName == packetFIX.Target &&
		        "0" == packetFIX.Encryption)
		    {
		        RetryStart = RetryMaximum = packetFIX.HeartBeatInterval;
                return true;
            }
            else
		    {
                var textMessage = new StringBuilder();
                textMessage.AppendLine("Invalid login response:");
                textMessage.AppendLine("  message type = " + packetFIX.MessageType);
                textMessage.AppendLine("  version = " + packetFIX.Version);
                textMessage.AppendLine("  sender = " + packetFIX.Sender);
                textMessage.AppendLine("  target = " + packetFIX.Target);
                textMessage.AppendLine("  encryption = " + packetFIX.Encryption);
                textMessage.AppendLine("  sequence = " + packetFIX.Sequence);
                textMessage.AppendLine("  heartbeat interval = " + packetFIX.HeartBeatInterval);
                textMessage.AppendLine(packetFIX.ToString());
                log.Error(textMessage.ToString());
                return false;
            }
		}

        protected override bool HandleLogon(MessageFIXT1_1 message)
        {
            if (ConnectionStatus != Status.PendingLogin)
            {
                throw new InvalidOperationException("Attempt logon when in " + ConnectionStatus +
                                                    " instead of expected " + Status.PendingLogin);
            }
            if (VerifyLoginAck(message))
            {
                return true;
            }
            else
            {
                RegenerateSocket();
                return false;
            }
        }
		
		protected override void ReceiveMessage(Message message) {
			var packetFIX = (MessageFIX4_4) message;
			switch( packetFIX.MessageType) {
                case "h":
                    if (ConnectionStatus == Status.PendingLogin)
                    {
                        StartRecovery();
                        RequestSessionUpdate();
                    }
                    SessionStatus(packetFIX);
			        break;
                case "AP":
				case "AO":
					PositionUpdate( packetFIX);
					break;
				case "8":
                    var transactTime = new TimeStamp(packetFIX.TransactionTime);
                    if( transactTime >= OrderStore.LastSequenceReset)
                    {
                        ExecutionReport(packetFIX);
                    }
                    else
                    {
                        if( debug) log.Debug("Ignoring execution report of sequence " + packetFIX.Sequence + " because transact time " + transactTime + " is earlier than last sequence reset " + OrderStore.LastSequenceReset);
                    }
					break;
				case "9":
					CancelRejected( packetFIX);
					break;
				case "1":
					SendHeartbeat();
					break;
				case "0":
					// Received heartbeat
					break;
				case "j":
					BusinessReject( packetFIX);
					break;
				default:
					log.Warn("Ignoring Message: '" + packetFIX.MessageType + "'\n" + packetFIX);
					break;
			}
		}

        private void BusinessReject(MessageFIX4_4 packetFIX) {
			var lower = packetFIX.Text.ToLower();
			var text = packetFIX.Text;
			var errorOkay = false;
			errorOkay = lower.Contains("server") ? true : errorOkay;
			errorOkay = text.Contains("DEMOORDS") ? true : errorOkay;
			errorOkay = text.Contains("FXORD1") ? true : errorOkay;
			errorOkay = text.Contains("FXORD2") ? true : errorOkay;
			errorOkay = text.Contains("FXORD01") ? true : errorOkay;
			errorOkay = text.Contains("FXORD02") ? true : errorOkay;
            log.Error(packetFIX.Text + " -- Sending EndBroker event.");
            TrySendEndBroker();
            log.Info(packetFIX.Text + " Sent EndBroker event due to Message:\n" + packetFIX);
            if (!errorOkay)
            {
				string message = "FIX Server reported an error: " + packetFIX.Text + "\n" + packetFIX;
				throw new ApplicationException( message);
			}
		}

        protected override void TryEndRecovery()
        {
            if (debug) log.Debug("TryEndRecovery Status " + ConnectionStatus + ", Session Status Online " + isOrderServerOnline + ", Resend Complete " + IsResendComplete);
            switch (ConnectionStatus)
            {
                case Status.Recovered:
                case Status.PendingLogOut:
                case Status.PendingLogin:
                case Status.Disconnected:
                    return;
                case Status.PendingRecovery:
                    if (IsResendComplete && isOrderServerOnline)
                    {
                        OrderStore.RequestSnapshot();
                        EndRecovery();
                        RequestPositions();
                        StartPositionSync();
                        return;
                    }
                    break;
                default:
                    throw new ApplicationException("Unexpected connection status for TryEndRecovery: " + ConnectionStatus);
            }
        }

        private string GetOpenOrders()
        {
            var sb = new StringBuilder();
            var list = OrderStore.GetOrders((x) => true);
			foreach( var order in list) {
				sb.Append( "    ");
				sb.Append( (string) order.BrokerOrder);
				sb.Append( " ");
				sb.Append( order);
				sb.AppendLine();
			}
            return sb.ToString();
        }
        private void StartPositionSync()
        {
            if( debug) log.Debug("StartPositionSync()");
            var openOrders = GetOpenOrders();
            if( string.IsNullOrEmpty(openOrders))
            {
                if( debug) log.Debug("Found 0 open orders.");
            }
            else
            {
			    if( debug) log.Debug("Recovered Open Orders:\n" + openOrders);
            }
			MessageFIXT1_1.IsQuietRecovery = false;
            foreach( var kvp in orderAlgorithms)
            {
                var algorithm = kvp.Value;
                algorithm.OrderAlgorithm.ProcessOrders();
                if (algorithm.OrderAlgorithm.IsPositionSynced)
                {
                    var symbol = Factory.Symbol.LookupSymbol(kvp.Key);
                    TrySendStartBroker(symbol);
                }
            }
        }

        private Dictionary<string,bool> sessionStatusMap = new Dictionary<string, bool>();
        private volatile bool isOrderServerOnline = false;
        private void SessionStatus(MessageFIX4_4 packetFIX)
        {
            var newIsSessionStatusOnline = false;
            log.Debug("Found session status for " + packetFIX.TradingSessionId + " or " + packetFIX.TradingSessionSubId +
                      ": " + packetFIX.TradingSessionStatus);
            var subId = string.IsNullOrEmpty(packetFIX.TradingSessionSubId)
                            ? packetFIX.TradingSessionId
                            : packetFIX.TradingSessionSubId;
            if( !CompareSession( subId) )
            {
                return;
            }
            switch (packetFIX.TradingSessionStatus)
            {
                case 2:
                    sessionStatusMap[subId] = true;
                    newIsSessionStatusOnline = true;
                    break;
                case 3:
                    sessionStatusMap[subId] = false;
                    break;
                default:
                    log.Warn("Received unknown server session status: " + packetFIX.TradingSessionStatus);
                    break;
            }
            foreach (var status in sessionStatusMap)
            {
                if (!status.Value)
                {
                    newIsSessionStatusOnline = false;
                }
            }
            if( debug) log.Debug("Order server connected (new " + newIsSessionStatusOnline + ", previous " + isOrderServerOnline);
            if (newIsSessionStatusOnline != isOrderServerOnline)
            {
                isOrderServerOnline = newIsSessionStatusOnline;
                if (isOrderServerOnline)
                {
                    CancelRecovered();
                    TryEndRecovery();
                }
                else
                {
                    var message = "Order server went offline. Attempting to reconnect.";
                    if( SyncTicks.Enabled)
                    {
                        log.Notice(message);
                    } else
                    {
                        log.Error(message);
                    }
                    CancelRecovered();
                    TrySendEndBroker();
                }
            }
            else if( trace)
            {
                var message = "Order server continues offline. Attempting to reconnect.";
                log.Trace(message);
            }
	    }

        public override void CancelRecovered()
        {
            base.CancelRecovered();
            foreach( var kvp in orderAlgorithms)
            {
                var algorithm = kvp.Value;
                algorithm.OrderAlgorithm.IsPositionSynced = false;
            }
        }

        private void PositionUpdate( MessageFIX4_4 packetFIX) {
			if( packetFIX.MessageType == "AO") {
				if(debug) log.Debug("PositionUpdate Complete.");
			}
            else 
            {
                var position = packetFIX.LongQuantity + packetFIX.ShortQuantity;
                SymbolInfo symbolInfo;
                try
                {
                    symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                }
                catch (ApplicationException ex)
                {
                    log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
                    return;
                }
                var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
                if (debug) log.Debug("PositionUpdate for " + symbolInfo + ": MBT actual =" + position + ", TZ actual=" + algorithm.OrderAlgorithm.ActualPosition);
            }
		}

        private void TryConfirmActive(SymbolInfo symbol, MessageFIX4_4 packetFIX)
        {
            var order = UpdateOrder(packetFIX, OrderState.PendingNew, null);
            if (order != null)
            {
                var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
                algorithm.OrderAlgorithm.ConfirmActive(order, IsRecovered);
            }
        }

        private void TryConfirmCreate(SymbolInfo symbol, MessageFIX4_4 packetFIX)
        {
            var order = UpdateOrder(packetFIX, OrderState.Active, null);
            if (order != null)
            {
                var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
                algorithm.OrderAlgorithm.ConfirmCreate(order, IsRecovered);
            }
        }
		
		private void ExecutionReport( MessageFIX4_4 packetFIX) {
            if (packetFIX.Text == "END")
            {
                throw new ApplicationException("Unexpected END in FIX Text field. Never sent a 35=AF message.");
            }
            else
            {
                CreateOrChangeOrder order;
                string orderStatus = packetFIX.OrderStatus;
                switch (orderStatus)
                {
                    case "0": // New
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport New: " + packetFIX);
                        }
                        SymbolInfo symbol = null;
                        try
                        {
                            symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                        }
                        catch (ApplicationException)
                        {
                            // symbol unknown.
                        }
                        if (symbol != null)
                        {
                            if (symbol.FixSimulationType == FIXSimulationType.ForexPair &&
                                OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out order) &&
                                (order.Type == OrderType.BuyStop || order.Type == OrderType.SellStop))
                            {
                                if (debug) log.Debug("New order message ignored for Forex Stop: " + order);
                            }
                            else
                            {
                                TryConfirmCreate(symbol, packetFIX);
                            }
                        }
                        break;
                    case "1": // Partial
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Partial: " + packetFIX);
                        }
                        UpdateOrder(packetFIX, OrderState.Active, null);
                        SendFill(packetFIX);
                        break;
                    case "2":  // Filled 
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Filled: " + packetFIX);
                        }
                        if (packetFIX.CumulativeQuantity < packetFIX.LastQuantity)
                        {
                            log.Warn("Ignoring message due to CumQty " + packetFIX.CumulativeQuantity + " less than " + packetFIX.LastQuantity + ". This is a workaround for a MBT FIX server which sends an extra invalid fill message on occasion.");
                            break;
                        }
                        order = UpdateOrder(packetFIX, OrderState.Active, null);
                        if( order != null && order.ReplacedBy != null)
                        {
                            OrderStore.RemoveOrder(order.ReplacedBy.BrokerOrder);
                        }
                        SendFill(packetFIX);
                        break;
                    case "5": // Replaced
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Replaced: " + packetFIX);
                        }
                        order = ReplaceOrder(packetFIX);
                        if (order != null)
                        {
                            var algorithm = GetAlgorithm(order.Symbol.BinaryIdentifier);
                            algorithm.OrderAlgorithm.ConfirmChange(order, IsRecovered);
                        }
                        else if (IsRecovered)
                        {
                            log.Warn("Changing order status after cancel/replace failed. Probably due to already being canceled or filled. Ignoring.");
                        }
                        break;
                    case "4": // Canceled
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Canceled: " + packetFIX);
                        }
                        {
                            var symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                            var algorithm = GetAlgorithm(symbolInfo.BinaryIdentifier);
                            CreateOrChangeOrder clientOrder;
                            if (!OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out clientOrder))
                            {
                                if (LogRecovery || !IsRecovery)
                                {
                                    log.Info("Cancel order for " + packetFIX.ClientOrderId +
                                             " was not found. Probably already canceled:\n" + GetOpenOrders());
                                }
                            }
                            CreateOrChangeOrder origOrder;
                            if (!OrderStore.TryGetOrderById(packetFIX.OriginalClientOrderId, out origOrder))
                            {
                                if (LogRecovery || !IsRecovery)
                                {
                                    log.Info("Orig order for " + packetFIX.OriginalClientOrderId +
                                        " was not found. Probably already canceled:\n" + GetOpenOrders());
                                }
                            }
                            if (clientOrder != null && clientOrder.ReplacedBy != null)
                            {
                                algorithm.OrderAlgorithm.ConfirmCancel(clientOrder, IsRecovered);
                            }
                            else if (origOrder != null && origOrder.ReplacedBy != null)
                            {
                                algorithm.OrderAlgorithm.ConfirmCancel(origOrder, IsRecovered);
                            }
                            else
                            {
                                if (debug) log.Debug("Cancel confirm message has neither client id nor original client id found order in cache with replaced by property set. Continuing with only original order.");
                                if (clientOrder != null)
                                {
                                    algorithm.OrderAlgorithm.ConfirmCancel(clientOrder, IsRecovered);
                                }
                                else if (origOrder != null)
                                {
                                    algorithm.OrderAlgorithm.ConfirmCancel(origOrder, IsRecovered);
                                }
                                else
                                {
                                    if (info) log.Info("Canceled message. Both clientOrder and origOrder were null. Likely normal when it occurs during FIX re-synchronizing. Client Id = " + packetFIX.ClientOrderId + ", Orig Id = " + packetFIX.OriginalClientOrderId);
                                }
                            }
                            break;
                        }
                    case "6": // Pending Cancel
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Pending Cancel: " + packetFIX);
                        }
                        if (!string.IsNullOrEmpty(packetFIX.Text) && packetFIX.Text.Contains("multifunction order"))
                        {
                            if (debug && (LogRecovery || IsRecovered))
                            {
                                log.Debug("Pending cancel of multifunction order, so removing " + packetFIX.ClientOrderId + " and " + packetFIX.OriginalClientOrderId);
                            }
                            order = OrderStore.RemoveOrder(packetFIX.ClientOrderId);
                            OrderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
                            break;
                        }
                        else
                        {
                            UpdateCancelOrder(packetFIX, OrderState.Pending);
                            TryHandlePiggyBackFill(packetFIX);
                        }
                        break;
                    case "8": // Rejected
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Reject: " + packetFIX);
                        }
                        RejectOrder(packetFIX);
                        break;
                    case "9": // Suspended
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Suspended: " + packetFIX);
                        }
                        UpdateOrder(packetFIX, OrderState.Suspended, packetFIX);
                        // Ignore 
                        break;
                    case "A": // PendingNew
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Pending New: " + packetFIX);
                        }
                        symbol = null;
                        try
                        {
                            symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                        }
                        catch (ApplicationException)
                        {
                            // symbol unknown.
                        }
                        if (symbol != null)
                        {
                            if (symbol.FixSimulationType == FIXSimulationType.ForexPair &&
                                OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out order) &&
                                (order.Type == OrderType.BuyStop || order.Type == OrderType.SellStop))
                            {
                                // Ignore any 
                                if (packetFIX.ExecutionType == "D")
                                {
                                    if (debug) log.Debug("Ignoring restated message 150=D for Forex stop execution report 39=A.");
                                }
                                else
                                {
                                    TryConfirmCreate(symbol, packetFIX);
                                }
                            }
                            else
                            {
                                TryConfirmActive(symbol, packetFIX);
                            }
                        }
                        break;
                    case "E": // Pending Replace
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Pending Replace: " + packetFIX);
                        }
                        var clientOrderId = packetFIX.ClientOrderId;
                        var orderState = OrderState.Pending;
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("PendingReplace( " + clientOrderId + ", state = " + orderState + ")");
                        }
                        UpdateOrReplaceOrder(packetFIX, packetFIX.OriginalClientOrderId, clientOrderId, orderState, null);
                        TryHandlePiggyBackFill(packetFIX);
                        break;
                    case "R": // Resumed.
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("ExecutionReport Resumed: " + packetFIX);
                        }
                        UpdateOrder(packetFIX, OrderState.Active, null);
                        // Ignore
                        break;
                    default:
                        throw new ApplicationException("Unknown order status: '" + orderStatus + "'");
                }
            }
		}

		private void TryHandlePiggyBackFill(MessageFIX4_4 packetFIX) {
			if( packetFIX.LastQuantity > 0) {
                if (debug) log.Debug("TryHandlePiggyBackFill triggering fill because LastQuantity = " + packetFIX.LastQuantity);
                SendFill(packetFIX);
			}
		}
		
		private void CancelRejected( MessageFIX4_4 packetFIX) {
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("CancelRejected: " + packetFIX);
			}
			string orderStatus = packetFIX.OrderStatus;
            CreateOrChangeOrder order;
		    bool removeOriginal = false;
		    OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out order);
			switch( orderStatus) {
				case "8": // Rejected
					var rejectReason = false;
                    if( packetFIX.Text.Contains("Order Server Not Available"))
                    {
                        rejectReason = true;
                        TrySendEndBroker();
                    }
                    else if( packetFIX.Text.Contains("Cannot cancel order. Probably already filled or canceled.") ||
                        packetFIX.Text.Contains("No such order"))
                    {
                        rejectReason = true;
                        removeOriginal = true;
                    }
                    else if( packetFIX.Text.Contains("Order pending remote") ||
                        packetFIX.Text.Contains("Cancel request already pending") ||
                        packetFIX.Text.Contains("ORDER in pending state") ||
                        packetFIX.Text.Contains("General Order Replace Error"))
                    {
                        rejectReason = true;
                    }

                    OrderStore.RemoveOrder(packetFIX.ClientOrderId);
                    if (removeOriginal)
                    {
                        OrderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
                    }
                    else
                    {
                        ResetFromPending(packetFIX.OriginalClientOrderId);
                    }
                    if (order != null)
                    {
                        var algo = GetAlgorithm(order.Symbol.BinaryIdentifier);
                        algo.OrderAlgorithm.RejectOrder(order, removeOriginal, IsRecovered);
                    }
                    else if( SyncTicks.Enabled )
                    {
                        var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                        var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                        tickSync.RemovePhysicalOrder();
                    }
                    if (!rejectReason && IsRecovered)
                    {
						var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
						var ignore = "The cancel reject error message '" + packetFIX.Text + "' was unrecognized. So it is being ignored. ";
						var handle = "If this reject causes any other problems please report it to have it added and properly handled.";
						log.Warn( message);
						log.Error( ignore + handle);
					} else {
						if( LogRecovery || !IsRecovery) {
							log.Info( "CancelReject(" + packetFIX.Text + ") Removed cancel order: " + packetFIX.ClientOrderId);
						}
					}
					break;
				default:
					throw new ApplicationException("Unknown cancel rejected order status: '" + orderStatus + "'");
			}
		}
		
		private bool GetLogicalOrderId( string clientOrderId, out int logicalOrderId) {
			logicalOrderId = 0;
			string[] parts = clientOrderId.Split(DOT_SEPARATOR);
			try {
				logicalOrderId = int.Parse(parts[0]);
			} catch( FormatException) {
				log.Warn("Fill received from order " + clientOrderId + " created externally. So it lacks any logical order id. That means a fill cannot be sent to the strategy. This will get resolved at next synchronization.");
				return false;
			}
			return true;
		}
		
		private int SideToSign( string side) {
			switch( side) {
				case "1": // Buy
					return 1;
				case "2": // Sell
				case "5": // SellShort
					return -1;
				default:
					throw new ApplicationException("Unknown order side: " + side);
			}
		}
		
		public void SendFill( MessageFIX4_4 packetFIX) {
			if( debug ) log.Debug("SendFill( " + packetFIX.ClientOrderId + ")");
			var symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
			var timeZone = new SymbolTimeZone(symbolInfo);
            var algorithm = GetAlgorithm(symbolInfo.BinaryIdentifier);
            var fillPosition = packetFIX.LastQuantity * SideToSign(packetFIX.Side);
            if (GetSymbolStatus(symbolInfo))
            {
                CreateOrChangeOrder order;
                if( OrderStore.TryGetOrderById( packetFIX.ClientOrderId, out order)) {
				    TimeStamp executionTime;
				    if( UseLocalFillTime) {
					    executionTime = TimeStamp.UtcNow;
				    } else {
					    executionTime = new TimeStamp(packetFIX.TransactionTime);
				    }
				    var configTime = executionTime;
				    configTime.AddSeconds( timeZone.UtcOffset(executionTime));
                    var fill = Factory.Utility.PhysicalFill(fillPosition, packetFIX.LastPrice, configTime, executionTime, order, false, packetFIX.OrderQuantity, packetFIX.CumulativeQuantity, packetFIX.LeavesQuantity, IsRecovered);
				    if( debug) log.Debug( "Sending physical fill: " + fill);
                    algorithm.OrderAlgorithm.ProcessFill(fill);
                }
                else
                {
                    algorithm.OrderAlgorithm.IncreaseActualPosition(fillPosition);
                    log.Notice("Fill id " + packetFIX.ClientOrderId + " not found. Must have been a manual trade.");
                    if( SyncTicks.Enabled)
                    {
                        var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                        tickSync.RemovePhysicalFill(packetFIX.ClientOrderId);
                    }
                }
                if (algorithm.OrderAlgorithm.IsPositionSynced)
                {
                    TrySendStartBroker(symbolInfo);
                }
			}
		}

        [Serializable]
        public class PhysicalOrderNotFoundException : Exception
	    {
	        public PhysicalOrderNotFoundException(string value) : base( value)
	        {
	            
	        }
    	}
		
		public void ProcessFill( SymbolInfo symbol, LogicalFillBinary fill)
		{
		    var symbolAlgorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if( symbolAlgorithm.IsBrokerStarted)
            {
                if (debug) log.Debug("Sending fill event for " + symbol + " to receiver: " + fill);
                while (!receiver.OnEvent(symbol, (int)EventType.LogicalFill, fill))
                {
                    Factory.Parallel.Yield();
                }
            }
            else
            {
                if (debug) log.Debug("Broker offline so fill not sent for " + symbol + " to receiver: " + fill);
                if( SyncTicks.Enabled)
                {
                    var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                    tickSync.RemovePhysicalFillWaiting(fill);
                }
            }
		}

		public void RejectOrder( MessageFIX4_4 packetFIX)
		{
		    var rejectReason = false;
		    bool removeOriginal = false;
		    if (packetFIX.Text.Contains("Cannot change order. Probably already filled or canceled.") ||
		        packetFIX.Text.Contains("No such order"))
		    {
		        rejectReason = true;
		        removeOriginal = true;
		    }
		    else if (packetFIX.Text.Contains("Outside trading hours") ||
		             packetFIX.Text.Contains("not accepted this session") ||
		             packetFIX.Text.Contains("Pending live orders") ||
		             packetFIX.Text.Contains("Trading temporarily unavailable") ||
		             packetFIX.Text.Contains("improper setting") ||
		             packetFIX.Text.Contains("No position to close"))
		    {
		        rejectReason = true;
		    }
            else if (packetFIX.Text.Contains("Order Server Not Available"))
            {
                rejectReason = true;
                TrySendEndBroker();
            }

		    if( IsRecovered && !rejectReason ) {
			    var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
			    var ignore = "The reject error message '" + packetFIX.Text + "' was unrecognized. So it is being ignored. ";
			    var handle = "If this reject causes any other problems please report it to have it added and properly handled.";
			    log.Warn( message);
			    log.Error( ignore + handle);
		    } else if( LogRecovery || IsRecovered) {
			    log.Info( "RejectOrder(" + packetFIX.Text + ") Removed order: " + packetFIX.ClientOrderId);
		    }

            CreateOrChangeOrder order;
            OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out order);
            if (order != null)
            {
                var algo = GetAlgorithm(order.Symbol.BinaryIdentifier);
                algo.OrderAlgorithm.RejectOrder(order, removeOriginal, IsRecovered);
            }
            else if( SyncTicks.Enabled )
            {
                var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                tickSync.RemovePhysicalOrder(order);
            }
		}
		
		private static readonly char[] DOT_SEPARATOR = new char[] { '.' };
		
		private OrderType GetOrderType(MessageFIX4_4 packetFIX) {
			var orderType = OrderType.None;
			switch( packetFIX.Side) {
				case "1":
					switch( packetFIX.OrderType) {
						case "1":
							orderType = OrderType.BuyMarket;
							break;
						case "2":
							orderType = OrderType.BuyLimit;
							break;
						case "3":
							orderType = OrderType.BuyStop;
							break;
						default:
							break;
					}
					break;
				case "2":
				case "5":
					switch( packetFIX.OrderType) {
						case "1":
							orderType = OrderType.SellMarket;
							break;
						case "2":
							orderType = OrderType.SellLimit;
							break;
						case "3":
							orderType = OrderType.SellStop;
							break;
						default:
							break;
					}
					break;
				default:
					throw new ApplicationException("Unknown order side: '" + packetFIX.Side + "'\n" + packetFIX);
			}
			return orderType;
		}

		private OrderSide GetOrderSide( MessageFIX4_4 packetFIX) {
			OrderSide side;
			switch( packetFIX.Side) {
				case "1":
					side = OrderSide.Buy;
					break;
				case "2":
					side = OrderSide.Sell;
					break;
				case "5":
					side = OrderSide.SellShort;
					break;
				default:
					throw new ApplicationException( "Unknown order side: " + packetFIX.Side + "\n" + packetFIX);
			}
			return side;
		}

		private int GetLogicalOrderId( MessageFIX4_4 packetFIX) {
			string[] parts = packetFIX.ClientOrderId.Split(DOT_SEPARATOR);
			int logicalOrderId = 0;
			try {
				logicalOrderId = int.Parse(parts[0]);
			} catch( FormatException) {
			}
			return logicalOrderId;
		}

        private void ResetFromPending(string clientOrderId)
        {
            CreateOrChangeOrder oldOrder = null;
            try
            {
                oldOrder = OrderStore.GetOrderById(clientOrderId);
                if( oldOrder.OrderState == OrderState.Pending || oldOrder.OrderState == OrderState.PendingNew)
                {
                    oldOrder.OrderState = OrderState.Active;
                }
            }
            catch (ApplicationException)
            {
                if (LogRecovery || !IsRecovery)
                {
                    log.Warn("Order ID# " + clientOrderId + " was not found for update or replace.");
                }
            }
        }

        private CreateOrChangeOrder UpdateOrder(MessageFIX4_4 packetFIX, OrderState orderState, object note)
        {
			var clientOrderId = packetFIX.ClientOrderId;
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("UpdateOrder( " + clientOrderId + ", state = " + orderState + ")");
			}
			return UpdateOrReplaceOrder( packetFIX, packetFIX.OriginalClientOrderId, clientOrderId, orderState, note);
		}
		
		public CreateOrChangeOrder ReplaceOrder( MessageFIX4_4 packetFIX) {
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("ReplaceOrder( " + packetFIX.OriginalClientOrderId + " => " + packetFIX.ClientOrderId + ")");
			}
			CreateOrChangeOrder order;
            if (!OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out order))
            {
                if (IsRecovery)
                {
                    order = UpdateOrReplaceOrder(packetFIX, packetFIX.ClientOrderId, packetFIX.ClientOrderId,
                                                 OrderState.Active, "ReplaceOrder");
                    return order;
                }
                else
                {
                    if (LogRecovery || !IsRecovery)
                    {
                        log.Warn("Order ID# " + packetFIX.ClientOrderId + " was not found for replace.");
                    }
                    return null;
                }
            }
            order.OrderState = OrderState.Active;
            int quantity = packetFIX.LeavesQuantity;
            if (quantity > 0)
            {
                if (info && (LogRecovery || !IsRecovery))
                {
                    if (debug)
                        log.Debug("Updated order: " + order + ".  Executed: " + packetFIX.CumulativeQuantity +
                                  " Remaining: " + packetFIX.LeavesQuantity);
                }
            }
            else
            {
                if (info && (LogRecovery || !IsRecovery))
                {
                    if (debug)
                        log.Debug("Order Completely Filled. Id: " + packetFIX.ClientOrderId + ".  Executed: " +
                                  packetFIX.CumulativeQuantity);
                }
            }
		    if( !packetFIX.ClientOrderId.Equals((string)order.BrokerOrder))
            {
                throw new InvalidOperationException("client order id mismatch with broker order property.");
            }
            OrderStore.SetSequences(RemoteSequence,FixFactory.LastSequence);
			return order;
		}
		
		private CreateOrChangeOrder UpdateOrReplaceOrder( MessageFIX4_4 packetFIX, string oldClientOrderId, string newClientOrderId, OrderState orderState, object note) {
			SymbolInfo symbolInfo;
			try {
				symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
			} catch( ApplicationException ex) {
				log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
				return null;
			}
            CreateOrChangeOrder order;
            long logicalSerialNumber = 0;
            CreateOrChangeOrder replacedBy;
            if (OrderStore.TryGetOrderById(newClientOrderId, out order))
            {
				logicalSerialNumber = order.LogicalSerialNumber;
                replacedBy = order.ReplacedBy;
            } else {
				if( debug && (LogRecovery || IsRecovered)) {
					log.Debug("Client Order ID# " + newClientOrderId + " was not found.");
				}
                return null;
            }
		    CreateOrChangeOrder oldOrder = null;
            if ( !string.IsNullOrEmpty(oldClientOrderId) && !OrderStore.TryGetOrderById(oldClientOrderId, out oldOrder))
            {
                if (LogRecovery || !IsRecovery)
                {
                    if( debug) log.Debug("Original Order ID# " + oldClientOrderId + " not found for update or replace. Normal.");
                }
            }
            int quantity = packetFIX.LeavesQuantity;
			var type = GetOrderType( packetFIX);
            if( type == OrderType.None)
            {
                type = order.Type;
            }
			var side = GetOrderSide( packetFIX);
			var logicalId = GetLogicalOrderId( packetFIX);
		    var flags = order.OrderFlags;
            order = Factory.Utility.PhysicalOrder(OrderAction.Create, orderState, symbolInfo, side, type, flags, packetFIX.Price, packetFIX.LeavesQuantity, logicalId, logicalSerialNumber, newClientOrderId, null, TimeStamp.UtcNow);
            order.ReplacedBy = replacedBy;
            order.OriginalOrder = oldOrder;
            if( oldOrder != null)
	        {
    	        if( debug && (LogRecovery || !IsRecovery)) log.Debug("Setting replace property of " + oldOrder.BrokerOrder + " to be replaced by " + order.BrokerOrder);
               	oldOrder.ReplacedBy = order;
            }
		    if( quantity > 0) {
				if( info && (LogRecovery || !IsRecovery) ) {
					if( debug) log.Debug("Updated order: " + order + ".  Executed: " + packetFIX.CumulativeQuantity + " Remaining: " + packetFIX.LeavesQuantity);
				}
			} else {
				if( info && (LogRecovery || !IsRecovery) ) {
					if( debug) log.Debug("Order completely filled or canceled. Id: " + packetFIX.ClientOrderId + ".  Executed: " + packetFIX.CumulativeQuantity);
				}
			}
            OrderStore.SetOrder(order);
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
			if( trace) {
				log.Trace("Updated order list:");
			    var logOrders = OrderStore.OrdersToString();
				log.Trace( "Broker Orders:\n" + logOrders);
			}
			return order;
		}

        public CreateOrChangeOrder UpdateCancelOrder(MessageFIX4_4 packetFIX, OrderState orderState)
        {
            var newClientOrderId = packetFIX.ClientOrderId;
            var oldClientOrderId = packetFIX.OriginalClientOrderId;
            SymbolInfo symbolInfo;
            try
            {
                symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
            }
            catch (ApplicationException ex)
            {
                log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
                return null;
            }
            CreateOrChangeOrder oldOrder;
            if (!OrderStore.TryGetOrderById(oldClientOrderId, out oldOrder))
            {
                if (debug && (LogRecovery || !IsRecovery))
                {
                    log.Debug("Original Order ID# " + oldClientOrderId + " not found for updating cancel order. Normal.");
                }
                return null;
            }
            CreateOrChangeOrder order;
            if (! OrderStore.TryGetOrderById(newClientOrderId, out order))
            {
                if (debug && (LogRecovery || IsRecovered))
                {
                    log.Debug("Client Order ID# " + newClientOrderId + " was not found. Recreating.");
                }
                order = Factory.Utility.PhysicalOrder(orderState, symbolInfo, oldOrder);
                order.BrokerOrder = newClientOrderId;
            }
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
            if (oldOrder != null)
            {
                if (debug && (LogRecovery || !IsRecovery)) log.Debug("Setting replace property of " + oldOrder.BrokerOrder + " to be replaced by " + order.BrokerOrder);
                oldOrder.ReplacedBy = order;
            }
            if (trace)
            {
                log.Trace("Updated order list:");
                var logOrders = OrderStore.OrdersToString();
                log.Trace("Broker Orders:\n" + logOrders);
            }
            return order;
        }

        private SymbolAlgorithm GetAlgorithm(string clientOrderId)
        {
			CreateOrChangeOrder origOrder;
			try {
				origOrder = OrderStore.GetOrderById(clientOrderId);
			} catch( ApplicationException) {
				throw new ApplicationException("Unable to find physical order by client id: " + clientOrderId);
			}
			return GetAlgorithm( origOrder.Symbol.BinaryIdentifier);
		}
		
		private SymbolAlgorithm GetAlgorithm(long symbol) {
            SymbolAlgorithm symbolAlgorithm;
			lock( orderAlgorithmsLocker) {
                if (!orderAlgorithms.TryGetValue(symbol, out symbolAlgorithm))
                {
					var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
				    var orderCache = Factory.Engine.LogicalOrderCache(symbolInfo, false);
					var algorithm = Factory.Utility.OrderAlgorithm( "mbtfix", symbolInfo, this, orderCache, OrderStore);
                    symbolAlgorithm = new SymbolAlgorithm {OrderAlgorithm = algorithm};
					orderAlgorithms.Add(symbol,symbolAlgorithm);
					algorithm.OnProcessFill = ProcessFill;
				}
			}
			return symbolAlgorithm;
		}
		
		private bool RemoveOrderHandler(long symbol) {
			lock( orderAlgorithmsLocker) {
				if( orderAlgorithms.ContainsKey(symbol)) {
					orderAlgorithms.Remove(symbol);
					return true;
				} else {
					return false;
				}
			}
		}

        public override void PositionChange(PositionChangeDetail positionChange)
		{
            var symbol = positionChange.Symbol;
            int desiredPosition = positionChange.Position;
            var inputOrders = positionChange.Orders;
            var strategyPositions = positionChange.StrategyPositions;
			if( debug) log.Debug( "PositionChange " + positionChange);
			
			var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            algorithm.OrderAlgorithm.SetDesiredPosition(desiredPosition);
            algorithm.OrderAlgorithm.SetStrategyPositions(strategyPositions);
            algorithm.OrderAlgorithm.SetLogicalOrders(inputOrders);
            if (!IsRecovered)
            {
                if (debug) log.Debug("PositionChange event received while FIX was offline or recovering. Skipping ProcessOrders. Current connection status is: " + ConnectionStatus);
                return;
            }
            //if (!algorithm.IsPositionSynced)
            //{
            algorithm.OrderAlgorithm.TrySyncPosition(strategyPositions);
            //}
            algorithm.OrderAlgorithm.ProcessOrders();
            if (algorithm.OrderAlgorithm.IsPositionSynced)
            {
                TrySendStartBroker(symbol);
            }
		}
		
		
	    protected override void Dispose(bool disposing)
	    {
            base.Dispose(disposing);
           	nextConnectTime = Factory.Parallel.TickCount + 10000;
	    }    
	        
		private int GetLogicalOrderId(int physicalOrderId) {
        	int logicalOrderId;
        	if( physicalToLogicalOrderMap.TryGetValue(physicalOrderId,out logicalOrderId)) {
        		return logicalOrderId;
        	} else {
        		return 0;
        	}
		}
		Dictionary<int,int> physicalToLogicalOrderMap = new Dictionary<int, int>();
	        
		public bool OnCreateBrokerOrder(CreateOrChangeOrder createOrChangeOrder)
		{
            if (!IsRecovered) return false;
            createOrChangeOrder.OrderState = OrderState.Pending;
			if( debug) log.Debug( "OnCreateBrokerOrder " + createOrChangeOrder + ". Connection " + ConnectionStatus + ", IsOrderServerOnline " + isOrderServerOnline);
            if( createOrChangeOrder.Action != OrderAction.Create)
            {
                throw new InvalidOperationException("Expected action Create but was " + createOrChangeOrder.Action);
            }
            OnCreateOrChangeBrokerOrder(createOrChangeOrder, false);
		    return true;
		}
	        
		private void OnCreateOrChangeBrokerOrder(CreateOrChangeOrder order, bool resend)
		{
            var fixMsg = (FIXMessage4_4)(resend ? FixFactory.Create(order.Sequence) : FixFactory.Create());
            order.Sequence = fixMsg.Sequence;
            OrderStore.SetOrder(order);
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);

            if (order.Size > order.Symbol.MaxOrderSize)
            {
                throw new ApplicationException("Order was greater than MaxOrderSize of " + order.Symbol.MaxPositionSize + " for:\n" + order);
            }

            var orderHandler = GetAlgorithm(order.Symbol.BinaryIdentifier);
		    var orderSize = order.Type == OrderType.SellLimit || order.Type == OrderType.SellMarket || order.Type == OrderType.SellStop ? -order.Size : order.Size;
            if (Math.Abs(orderHandler.OrderAlgorithm.ActualPosition + orderSize) > order.Symbol.MaxPositionSize)
            {
                throw new ApplicationException("Order was greater than MaxPositionSize of " + order.Symbol.MaxPositionSize + " for:\n" + order);
            }

			if( debug) log.Debug( "Adding Order to open order list: " + order);
			if( order.Action == OrderAction.Change) {
                var origBrokerOrder = order.OriginalOrder.BrokerOrder;
                fixMsg.SetClientOrderId(order.BrokerOrder);
				fixMsg.SetOriginalClientOrderId(origBrokerOrder);
			    CreateOrChangeOrder origOrder;
				if( OrderStore.TryGetOrderById(origBrokerOrder, out origOrder))
				{
                    origOrder.ReplacedBy = order;
				    if (debug) log.Debug("Setting replace property of " + origBrokerOrder + " to " + order.BrokerOrder);
                }
			} else {
				fixMsg.SetClientOrderId(order.BrokerOrder);
			}
			fixMsg.SetAccount(AccountNumber);
            if (order.Action == OrderAction.Change)
            {
				fixMsg.AddHeader("G");
			} else {
				fixMsg.AddHeader("D");
				if( order.Symbol.Destination.ToLower() == "default") {
					fixMsg.SetDestination("MBTX");
				} else {
					fixMsg.SetDestination(order.Symbol.Destination);
				}
			}
			fixMsg.SetHandlingInstructions(1);
			fixMsg.SetSymbol(order.Symbol.Symbol);
			fixMsg.SetSide( GetOrderSide(order.Side));
			switch( order.Type) {
				case OrderType.BuyLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(order.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.BuyMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.BuyStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(order.Price);
					fixMsg.SetStopPrice(order.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.SellLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(order.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.SellMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.SellStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(order.Price);
					fixMsg.SetStopPrice(order.Price);
					fixMsg.SetTimeInForce(1);
					break;
			}
			fixMsg.SetLocateRequired("N");
			fixMsg.SetTransactTime(order.UtcCreateTime);
			fixMsg.SetOrderQuantity((int)order.Size);
			fixMsg.SetOrderCapacity("A");
			fixMsg.SetUserName();
            if (order.Action == OrderAction.Change)
            {
				if( verbose) log.Verbose("Change order: \n" + fixMsg);
			} else {
                if (verbose) log.Verbose("Create new order: \n" + fixMsg);
			}
            if( resend)
            {
                fixMsg.SetDuplicate(true);
            }
			SendMessage( fixMsg);
		}

		private int GetOrderSide( OrderSide side) {
			switch( side) {
				case OrderSide.Buy:
					return 1;
				case OrderSide.Sell:
					return 2;
				case OrderSide.SellShort:
					return 5;
				case OrderSide.SellShortExempt:
					return 6;
				default:
					throw new ApplicationException("Unknown OrderSide: " + side);
			}
		}
		

		private long GetUniqueOrderId() {
			return TimeStamp.UtcNow.Internal;
		}

        protected override void ResendOrder(CreateOrChangeOrder order)
        {
            if( order.Action == OrderAction.Cancel)
            {
                if (debug) log.Debug("Resending cancel order: " + order);
                //if (SyncTicks.Enabled && !IsRecovered)
                //{
                //    TryAddPhysicalOrder(order);
                //}
                SendCancelOrder(order, true);
            }
            else
            {
                if (debug) log.Debug("Resending order: " + order);
                //if (SyncTicks.Enabled && !IsRecovered)
                //{
                //    TryAddPhysicalOrder(order);
                //}
                OnCreateOrChangeBrokerOrder(order, true);
            }
        }

		public bool OnCancelBrokerOrder(CreateOrChangeOrder order)
		{
            if (!IsRecovered) return false;
            if (debug) log.Debug("OnCancelBrokerOrder " + order + ". Connection " + ConnectionStatus + ", IsOrderServerOnline " + isOrderServerOnline);
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
            CreateOrChangeOrder createOrChangeOrder;
			try {
                createOrChangeOrder = OrderStore.GetOrderById(order.OriginalOrder.BrokerOrder);
			} catch( ApplicationException ex) {
                if (LogRecovery || !IsRecovery)
                {
                    log.Info("Order probably already canceled. " + ex.Message);
                }
			    if( SyncTicks.Enabled) {
					var tickSync = SyncTicks.GetTickSync(order.Symbol.BinaryIdentifier);
					tickSync.RemovePhysicalOrder();
				}
				return true;
			}
            createOrChangeOrder.ReplacedBy = order;
		    if( !object.ReferenceEquals(order.OriginalOrder,createOrChangeOrder))
            {
                throw new ApplicationException("Different objects!");
            }

            SendCancelOrder(order, false);
            return true;

		}

        private void TryAddPhysicalOrder( CreateOrChangeOrder order)
        {
            var tickSync = SyncTicks.GetTickSync(order.Symbol.BinaryIdentifier);
            tickSync.AddPhysicalOrder(order);
        }

        private void SendCancelOrder( CreateOrChangeOrder order, bool resend)
        {
            var fixMsg = (FIXMessage4_4)(resend ? FixFactory.Create(order.Sequence) : FixFactory.Create());
            order.Sequence = fixMsg.Sequence;
            OrderStore.SetOrder(order);
            string newClientOrderId = order.BrokerOrder;
            fixMsg.SetOriginalClientOrderId((string)order.OriginalOrder.BrokerOrder);
            fixMsg.SetClientOrderId(newClientOrderId);
            fixMsg.SetAccount(AccountNumber);
            fixMsg.SetSide(GetOrderSide(order.OriginalOrder.Side));
            fixMsg.AddHeader("F");
            fixMsg.SetSymbol(order.Symbol.Symbol);
            fixMsg.SetTransactTime(TimeStamp.UtcNow);
            if (resend)
            {
                fixMsg.SetDuplicate(true);
            }
            SendMessage(fixMsg);
        }
		
		public bool OnChangeBrokerOrder(CreateOrChangeOrder createOrChangeOrder)
		{
            if (!IsRecovered) return false;
            createOrChangeOrder.OrderState = OrderState.Pending;
            if (debug) log.Debug("OnChangeBrokerOrder( " + createOrChangeOrder + ". Connection " + ConnectionStatus + ", IsOrderServerOnline " + isOrderServerOnline);
            if (createOrChangeOrder.Action != OrderAction.Change)
            {
                throw new InvalidOperationException("Expected action Change but was " + createOrChangeOrder.Action);
            }
            OnCreateOrChangeBrokerOrder(createOrChangeOrder, false);
            return true;
		}

	    public bool HasBrokerOrder(CreateOrChangeOrder order)
	    {
	        CreateOrChangeOrder queueOrder;
            if( OrderStore.TryGetOrderBySerial(order.LogicalSerialNumber, out queueOrder))
            {
                switch (queueOrder.OrderState)
                {
                    case OrderState.PendingNew:
                    case OrderState.Pending:
                    case OrderState.Active:
                        return true;
                    case OrderState.Filled:
                    case OrderState.Suspended:
                        return false;
                    default:
                        throw new InvalidOperationException("Unknow order state: " + order.OrderState);
                }
            } else
            {
                return false;
            }
	    }
	}
}