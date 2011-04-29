﻿using System;
using System.Collections.Generic;
using System.Text;
using TickZoom.Api;

namespace TickZoom.MBTFIX
{
    public class PhysicalOrderStore
    {
        private static readonly Log log = Factory.SysLog.GetLogger(typeof(MBTFIXProvider));
        private static readonly bool info = log.IsDebugEnabled;
        private static readonly bool debug = log.IsDebugEnabled;
        private static readonly bool trace = log.IsTraceEnabled;
        private Dictionary<string, PhysicalOrder> ordersByBrokerId = new Dictionary<string, PhysicalOrder>();
        private Dictionary<long, PhysicalOrder> ordersBySerial = new Dictionary<long, PhysicalOrder>();
        private TaskLock ordersLocker = new TaskLock();

        public void Clear()
        {
            log.Info("Clearing all orders.");
            using( ordersLocker.Using())
            {
                ordersByBrokerId.Clear();
                ordersBySerial.Clear();
            }
        }

        public bool TryGetOrderById(object brokerOrder, out PhysicalOrder order)
        {
            using (ordersLocker.Using())
            {
                return ordersByBrokerId.TryGetValue((string) brokerOrder, out order);
            }
        }

        public PhysicalOrder GetOrderById(object brokerOrder)
        {
            using (ordersLocker.Using())
            {
                PhysicalOrder order;
                if (!ordersByBrokerId.TryGetValue((string) brokerOrder, out order))
                {
                    throw new ApplicationException("Unable to find order for id: " + brokerOrder);
                }
                return order;
            }
        }

        public PhysicalOrder RemoveOrder(string clientOrderId)
        {
            if (string.IsNullOrEmpty(clientOrderId))
            {
                return null;
            }
            using (ordersLocker.Using())
            {
                PhysicalOrder order = null;
                if (ordersByBrokerId.TryGetValue(clientOrderId, out order))
                {
                    var result = ordersByBrokerId.Remove(clientOrderId);
                    if( trace) log.Trace("Removed " + clientOrderId);
                    PhysicalOrder orderBySerial;
                    if( ordersBySerial.TryGetValue(order.LogicalSerialNumber, out orderBySerial))
                    {
                        if( orderBySerial.BrokerOrder.Equals(clientOrderId))
                        {
                            ordersBySerial.Remove(order.LogicalSerialNumber);
                            if( trace) log.Trace("Removed " + order.LogicalSerialNumber);
                        }
                    }
                }
                return order;
            }
        }

        //public Dictionary<string, PhysicalOrder>.Enumerator GetEnumerator()
        //{
        //    return ordersByBrokerId.GetEnumerator();
        //}

        public bool TryGetOrderBySerial(long logicalSerialNumber, out PhysicalOrder order)
        {
            using (ordersLocker.Using())
            {
                return ordersBySerial.TryGetValue(logicalSerialNumber, out order);
            }
        }

        public PhysicalOrder GetOrderBySerial(long logicalSerialNumber)
        {
            using (ordersLocker.Using())
            {
                PhysicalOrder order;
                if (!ordersBySerial.TryGetValue(logicalSerialNumber, out order))
                {
                    throw new ApplicationException("Unable to find order by serial for id: " + logicalSerialNumber);
                }
                return order;
            }
        }
        public void AssignById(string newClientOrderId, PhysicalOrder order)
        {
            using (ordersLocker.Using())
            {
                if( trace) log.Trace("Assigning order " + newClientOrderId + " with " + order.LogicalSerialNumber);
                ordersByBrokerId[newClientOrderId] = order;
                if( order.LogicalSerialNumber != 0)
                {
                    ordersBySerial[order.LogicalSerialNumber] = order;
                }
            }
        }

        public List<PhysicalOrder> GetOrders(Func<PhysicalOrder,bool> select)
        {
            using (ordersLocker.Using())
            {
                var list = new List<PhysicalOrder>();
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    if (select(order))
                    {
                        list.Add(order);
                    }
                }
                return list;
            }
        }

        public string LogOrders()
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
    }
}