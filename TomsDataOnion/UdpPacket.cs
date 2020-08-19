using System;
using System.Linq;
using System.IO;
using System.Net;

namespace TomsDataOnion
{
  public class UdpPacket
  {
    public ushort SourcePort { get; }
    public ushort DestPort { get; }
    public ushort Length { get; }
    public ushort Checksum { get; }
    public byte[] Data { get; }

    public UdpPacket(byte[] bytes)
    {
      if (bytes.Length < 8)
        throw new InvalidDataException("UDP Packet expects at least 8 bytes");

      var stream = new MemoryStream(bytes, 0, bytes.Length);
      var reader = new BinaryReader(stream);

      this.SourcePort = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      this.DestPort = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      this.Length = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      this.Checksum = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());

      this.Data = reader.ReadBytes(this.Length - 8);
    }

    public bool IsValidChecksum(IPAddress srcIP, IPAddress destIP)
    {
      return this.IsValidChecksum(srcIP.AsUintNetworkOrder(), destIP.AsUintNetworkOrder());
    }

    public bool IsValidChecksum(uint srcIP, uint destIP)
    {
      var udpProtocol = 17;
      int sum = this.SourcePort + this.DestPort + this.Length +
        // ipv4 pseudo header fields
        (ushort)(srcIP & 0xffff) + (ushort)(srcIP >> 16) +
        (ushort)(destIP & 0xffff) + (ushort)(destIP >> 16) +
        this.Length + // pretend the ipv4 length is the same as udp's (?!!)
        udpProtocol;

      for (var i = 0; i < this.Data.Length; i += 2)
      {
        var hi = this.Data[i];
        var lo = i + 1 < this.Data.Length ? this.Data[i + 1] : 0;

        sum += (ushort)(hi << 8 | lo);
      }

      while (sum > 0xffff)
        sum = (sum & 0xffff) + (sum >> 16);

      return this.Checksum == (ushort)~sum;
    }
  }

  public static class IPAddressExtender
  {
    public static uint AsUintNetworkOrder(this IPAddress addr)
    {
      return (uint)IPAddress.HostToNetworkOrder(BitConverter.ToInt32(addr.GetAddressBytes(), 0));
    }
  }
}
