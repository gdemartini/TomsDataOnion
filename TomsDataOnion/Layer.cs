using System;
using System.IO;
using System.Text;

namespace TomsDataOnion
{
  public class Layer
  {
    public string Info { get; set; } = "";
    public string Payload { get; set; } = "";

    public byte[] PayloadAsBytes
    {
      get
      {
        var decoder = new Ascii85 { LineLength = 60 };
        return decoder.Decode(this.Payload);
      }
    }

    public override string ToString()
    {
      return this.Info + "Payload: " + this.GetPayloadShort();
    }

    private string GetPayloadShort()
    {
      return this.Payload.Length <= 50 ? this.Payload : $"{this.Payload[..23]}...{this.Payload[^23..]}".Replace("\n", "");
    }

    public Layer Peel(Func<byte[], byte[]> func)
    {
      var newLayer = Encoding.ASCII.GetString(func(this.PayloadAsBytes));
      return Parse(new StringReader(newLayer));
    }

    public static Layer Parse(TextReader tr)
    {
      var s = tr.ReadLine();
      while (s != null && !s.StartsWith("==[ Layer") && !s.StartsWith("==[ The Core ]"))
      {
        s = tr.ReadLine();
      }

      if (s == null)
        throw new InvalidDataException("Layer definition not found");

      var sb = new StringBuilder();
      while (s != null && !s.StartsWith("==[ Payload ]"))
      {
        sb.AppendLine(s);

        s = tr.ReadLine();
      }

      return new Layer { Info = sb.ToString(), Payload = tr. ReadToEnd().Trim() };
    }
  }
}
