using System.ComponentModel;
using NewLife.IoT.Drivers;

namespace NewLife.OPC.Drivers;

/// <summary>OPC参数</summary>
public class OpcUaParameter : IDriverParameter
{
    /// <summary>地址。opcua://{ip}/{name}</summary>
    [Description("地址。opcua://{ip}/{name}")]
    public String Address { get; set; } = "opcua://{ip}/{name}";
}