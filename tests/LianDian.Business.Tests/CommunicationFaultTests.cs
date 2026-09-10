using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using LianDian.Comm;
using LianDian.Core.Config;
using NModbus;
using Xunit;

namespace LianDian.Business.Tests
{
    public class CommunicationFaultTests
    {
        [Theory]
        [InlineData("slave", false)]
        [InlineData("argument", false)]
        [InlineData("io", true)]
        [InlineData("timeout", true)]
        [InlineData("socket", true)]
        public void OnlyTransportFaultsDisconnect(string kind, bool disconnect)
        {
            Exception fault = kind == "slave" ? (Exception)new SlaveException() :
                kind == "argument" ? new ArgumentException("invalid") :
                kind == "io" ? new IOException("broken") :
                kind == "timeout" ? new TimeoutException() : new SocketException();
            // No Connect or network I/O: inject an exception at the Execute boundary.
            using (var tcp = new TcpClient())
            using (var client = new ModbusTcpPlcClient(new PlcConfig()))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ModbusTcpPlcClient).GetField("_master", flags).SetValue(client, new ModbusFactory().CreateMaster(tcp));
                typeof(ModbusTcpPlcClient).GetField("<IsConnected>k__BackingField", flags).SetValue(client, true);
                var execute = typeof(ModbusTcpPlcClient).GetMethod("Execute", flags).MakeGenericMethod(typeof(int));
                var thrown = Assert.Throws<TargetInvocationException>(() => execute.Invoke(client, new object[] { new Func<int>(() => throw fault) }));
                Assert.Same(fault, thrown.InnerException);
                Assert.Equal(!disconnect, client.IsConnected);
            }
        }
    }
}
