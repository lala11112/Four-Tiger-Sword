using System;
using System.Security.Cryptography;
using System.Text;

namespace AssetInventory
{
    internal static class SemanticVectorUtils
    {
        public static float[] Normalize(float[] vector)
        {
            if (vector == null || vector.Length == 0) return vector;

            double sum = 0d;
            for (int i = 0; i < vector.Length; i++) sum += vector[i] * vector[i];
            double length = Math.Sqrt(sum);
            if (length <= double.Epsilon) return vector;

            float inv = (float)(1d / length);
            float[] result = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++) result[i] = vector[i] * inv;
            return result;
        }

        public static float Dot(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;
            double sum = 0d;
            for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return (float)sum;
        }

        public static byte[] ToBytes(float[] vector)
        {
            if (vector == null) return null;

            byte[] result = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, result, 0, result.Length);
            return result;
        }

        public static float[] FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length % sizeof(float) != 0) return Array.Empty<float>();

            float[] result = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        public static string HashText(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
