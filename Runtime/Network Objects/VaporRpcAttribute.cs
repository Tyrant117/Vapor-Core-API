using System;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    [AttributeUsage(AttributeTargets.Method)]
    public class VaporRpcAttribute : Attribute
    {
        public SendTo SendTo { get; }
        public NetworkDelivery NetworkDelivery { get; }
        
        public VaporRpcAttribute(SendTo sendTo, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced)
        {
            SendTo = sendTo;
            NetworkDelivery = networkDelivery;
        }
    }
}