using System.IO;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    public static class WavEncoder
    {
        public static byte[] Encode(AudioClip clip)
        {
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int dataLength = samples.Length * 2;
                int fileLength = 36 + dataLength;

                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(fileLength);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)clip.channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2);
                writer.Write((short)(clip.channels * 2));
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);

                foreach (float sample in samples)
                {
                    short value = (short)Mathf.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
                    writer.Write(value);
                }

                return stream.ToArray();
            }
        }
    }
}
