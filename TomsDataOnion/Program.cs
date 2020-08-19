using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TomsDataOnion
{
  public class Program
  {
    public static void Main(string[] args)
    {
      //var bytes = new byte[] { 69, 0, 0, 89, 0, 0, 64, 0, 32, 17, 67, 193, 10, 1, 1, 10, 10, 1, 1, 200, 41, 166, 164, 85, 0, 69, 78, 124, 61, 61, 91, 32, 76, 97, 121, 101, 114, 32, 53, 47, 54, 58, 32, 65, 100, 118, 97, 110, 99, 101, 100, 32, 69, 110, 99, 114, 121, 112, 116, 105, 111, 110, 32, 83, 116, 97, 110, 100, 97, 114, 100, 32, 93, 61, 61, 61, 61, 61, 61, 61, 61, 61, 61, 61, 61, 61, 61, 61, 10 };

      //var ipheader = new IPv4Header(bytes[..20]);
      //Console.WriteLine(ipheader.IsValidChecksum);

      //var udp = new UdpPacket(bytes[20..]);
      //Console.WriteLine(udp.IsValidChecksum(ipheader.SourceIP, ipheader.DestIP));
      //Console.WriteLine(Encoding.ASCII.GetString(udp.Data));

      var layer = Layer.Parse(new StreamReader(GetLayer0()));
      Console.WriteLine(layer);

      layer = layer.Peel(Noop);
      Console.WriteLine(layer);

      layer = layer.Peel(FlipEvenBitsAndRotate);
      Console.WriteLine(layer);

      layer = layer.Peel(CheckParity);
      Console.WriteLine(layer);

      layer = layer.Peel(XorDecrypt);
      Console.WriteLine(layer);

      layer = layer.Peel(UnpackNetwork);
      Console.WriteLine(layer);

      //Console.WriteLine(Encoding.ASCII.GetString(layer.PayloadAsBytes));
    }

    private static Stream GetLayer0()
    {
      var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      return File.OpenRead(Path.Combine(exePath, "layer0.txt"));
    }

    private static byte[] Noop(byte[] bytes)
    {
      return bytes;
    }

    private static byte[] FlipEvenBitsAndRotate(byte[] bytes)
    {
      for (int i = 0; i < bytes.Length; i++)
      {
        var x = bytes[i];
        x = (byte)((x & 0b10101010) | (~x & 0b01010101)); // Flip even bits
        bytes[i] = (byte)((x >> 1) + ((x & 1) << 7));     // Rotate right
      }
      return bytes;
    }

    private static byte[] CheckParity(byte[] bytes)
    {
      var m = new MemoryStream();
      byte tmp = 0;
      var bufferedBits = 0;

      foreach (var x in bytes.Where(x => ParityMatches(x)))
      {
        // add (8 - bufferedBits) bits to the buffer
        tmp |= (byte)((x & ~1) >> bufferedBits);

        if (bufferedBits != 0) // The only scenario where we don't wanna flush is when the buffer was empty
        {
          m.WriteByte(tmp);
          tmp = (byte)((x & ~1) << (8 - bufferedBits));
        }

        bufferedBits = (bufferedBits + 7) % 8;
      }

      return m.ToArray();
    }

    private static bool ParityMatches(byte x)
    {
      var parity = x & 1;
      var c = 0;
      x >>= 1;
      while (x > 0)
      {
        c += x & 1;
        x >>= 1;
      }
      return c % 2 == parity;
    }


    private static byte[] XorDecrypt(byte[] bytes)
    {
      // Find the key
      var key = new byte[32];
      var known = Encoding.ASCII.GetBytes("==[ Layer 4/6: Network Traffic ]");
      var sample = bytes.Take(known.Length).ToArray();
      for (int i = 0; i < known.Length; i++)
      {
        while ((sample[i] ^ key[i]) != known[i])
          key[i]++;
      }

      // Decrypt using key
      var ki = 0;
      for (int i = 0; i < bytes.Length; i++)
      {
        bytes[i] = (byte)(bytes[i] ^ key[ki]);
        ki = (ki + 1) % key.Length;
      }

      return bytes;
    }

    private static byte[] UnpackNetwork(byte[] bytes)
    {
      var result = new MemoryStream();

      var stream = new MemoryStream(bytes, 0, bytes.Length);
      var reader = new BinaryReader(stream);

      while (stream.Position < stream.Length)
      {
        var ipv4 = new IPv4Header(reader.ReadBytes(20));
        var udp = new UdpPacket(reader.ReadBytes(ipv4.Length - 20));

        //- The packet was sent FROM any port of 10.1.1.10
        //- The packet was sent TO port 42069 of 10.1.1.200
        //- The IPv4 header checksum is correct
        //- The UDP header checksum is correct
        if (ipv4.SourceIP.ToString() == "10.1.1.10" &&
          ipv4.DestIP.ToString() == "10.1.1.200" && udp.DestPort == 42069 &&
          ipv4.IsValidChecksum &&
          udp.IsValidChecksum(ipv4.SourceIP, ipv4.DestIP))
        {
          result.Write(udp.Data);
        }
        //else
        //{
        //  Console.WriteLine($"dropped: {Encoding.ASCII.GetString(udp.Data)}");
        //}
      }
      return result.ToArray();
    }
  }
}