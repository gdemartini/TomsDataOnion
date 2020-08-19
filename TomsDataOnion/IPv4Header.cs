using System;
using System.IO;
using System.Net;

namespace TomsDataOnion
{
  public class IPv4Header
  {
    public byte VersionIHL { get; }
    public byte TypeOfService { get; }
    public ushort Length { get; }
    public ushort Id { get; }
    public ushort FlagsOffset { get; }
    public byte Ttl { get; }
    public byte Protocol { get; }
    public ushort Checksum { get; }
    public IPAddress SourceIP { get; }
    public IPAddress DestIP { get; }

    public bool IsValidChecksum { get; }

    public IPv4Header(byte[] bytes)
    {
      if (bytes.Length != 20)
        throw new InvalidDataException("IPv4 header expects 20 bytes");

      var stream = new MemoryStream(bytes, 0, bytes.Length);
      var reader = new BinaryReader(stream);

      this.VersionIHL = reader.ReadByte();
      this.TypeOfService = reader.ReadByte();
      this.Length = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      this.Id = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
      this.FlagsOffset = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());

      this.Ttl = reader.ReadByte();
      this.Protocol = reader.ReadByte();
      this.Checksum = reader.ReadUInt16();

      this.SourceIP = new IPAddress(reader.ReadUInt32());
      this.DestIP = new IPAddress(reader.ReadUInt32());

      stream.Position = 0;
      int sum = 0;
      for (int i = 0; i < bytes.Length / 2; i++)
      {
        sum += reader.ReadUInt16();
      }

      while (sum > 0xffff)
        sum = (sum & 0xffff) + (sum >> 16);
      this.IsValidChecksum = sum == 0xffff;
    }
  }
}
