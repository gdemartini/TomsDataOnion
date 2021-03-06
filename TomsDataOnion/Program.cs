﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

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

      //var helloWorld = new byte[]
      //{
      //  0x50, 0x48, 0xC2, 0x02, 0xA8, 0x4D, 0x00, 0x00, 0x00, 0x4F, 0x02, 0x50, 0x09, 0xC4, 0x02, 0x02, 0xE1, 0x01, 0x4F, 0x02, 0xC1, 0x22, 0x1D, 0x00, 0x00, 0x00, 0x48, 0x30, 0x02, 0x58, 0x03, 0x4F, 0x02, 0xB0, 0x29, 0x00, 0x00, 0x00, 0x48, 0x31, 0x02, 0x50, 0x0C, 0xC3, 0x02, 0xAA, 0x57, 0x48, 0x02, 0xC1, 0x21, 0x3A, 0x00, 0x00, 0x00, 0x48, 0x32, 0x02, 0x48, 0x77, 0x02, 0x48, 0x6F, 0x02, 0x48, 0x72, 0x02, 0x48, 0x6C, 0x02, 0x48, 0x64, 0x02, 0x48, 0x21, 0x02, 0x01, 0x65, 0x6F, 0x33, 0x34, 0x2C
      //};

      //var vm = new TomtelEmulator(helloWorld, null, Console.Out);
      //vm.Run(100000);

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

      layer = layer.Peel(DecryptAes);
      Console.WriteLine(layer);

      layer = layer.Peel(VirtualMachine);
      Console.WriteLine(layer);
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
        key[i] = (byte)(sample[i] ^ known[i]);
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

    private static byte[] DecryptAes(byte[] bytes)
    {
      //- First 32 bytes: The 256-bit key encrypting key (KEK).
      var kek = bytes[0..32];
      //- Next 8 bytes: The 64-bit initialization vector (IV) for the wrapped key.
      var kiv = bytes[32..40];
      //- Next 40 bytes: The wrapped (encrypted) key. When decrypted, this will become the 256-bit encryption key.
      var wrappedKey = bytes[40..80];
      //- Next 16 bytes: The 128-bit initialization vector (IV) for the encrypted payload.
      var iv = bytes[80..96];
      //- All remaining bytes: The encrypted payload.
      var encPayload = bytes[96..];

      // Unwrap key
      var aesWrapEngine = new AesWrapEngine();
      aesWrapEngine.Init(false, new ParametersWithIV(new KeyParameter(kek), kiv));
      var key = aesWrapEngine.Unwrap(wrappedKey, 0, wrappedKey.Length);

      // Initialize AES CTR (counter) mode cipher
      var cipher = CipherUtilities.GetCipher("AES/CTR/NoPadding");
      cipher.Init(false, new ParametersWithIV(new KeyParameter(key), iv));

      return cipher.DoFinal(encPayload);
    }

    private static byte[] VirtualMachine(byte[] bytes)
    {
      var result = new MemoryStream();
      var vm = new TomtelEmulator(bytes, result);
      vm.Run();
      return result.ToArray();
    }
  }
}