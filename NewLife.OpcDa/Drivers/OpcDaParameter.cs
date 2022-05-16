using System.ComponentModel;
using NewLife.IoT.Drivers;

namespace NewLife.OPC.Drivers;

/// <summary>OPC参数</summary>
public class OpcDaParameter : IDriverParameter
{
    /// <summary>地址。opcda://{ip}/{name}</summary>
    [Description("地址。opcda://{ip}/{name}")]
    public String Address { get; set; } = "opcda://{ip}/{name}";
}