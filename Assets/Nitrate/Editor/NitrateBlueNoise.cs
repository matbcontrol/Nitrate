using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nitrate.EditorTools
{
    /// <summary>
    /// Generates a tiling 64x64 blue noise texture with the void-and-cluster method (Ulichney, 1993), so the project
    /// needs no downloaded noise. Every pixel gets a rank 0..4095 such that, for any threshold, the pixels below it are
    /// spread as evenly as possible: the noise has no low-frequency clumps, which is why it makes fine, even grain
    /// and jitter that blurs away cleanly.
    /// </summary>
    public static class NitrateBlueNoise
    {
        const int k_Size = 64;
        const float k_Sigma = 1.9f;

        [MenuItem("Nitrate/Generate Blue Noise")]
        public static Texture2D Generate() => Generate("Assets/Nitrate/Textures/BlueNoise64.png");

        public static Texture2D Generate(string path)
        {
            int n = k_Size * k_Size;
            // Gaussian energy kernel on a torus, so the texture tiles without seams.
            var kernel = new float[k_Size * k_Size];
            for (int y = 0; y < k_Size; y++)
            for (int x = 0; x < k_Size; x++)
            {
                int dx = Mathf.Min(x, k_Size - x), dy = Mathf.Min(y, k_Size - y);
                kernel[y * k_Size + x] = Mathf.Exp(-(dx * dx + dy * dy) / (2f * k_Sigma * k_Sigma));
            }

            var ones = new bool[n];
            var energy = new float[n];
            void Toggle(int p, bool on)
            {
                ones[p] = on;
                int px = p % k_Size, py = p / k_Size;
                float sign = on ? 1f : -1f;
                for (int y = 0; y < k_Size; y++)
                for (int x = 0; x < k_Size; x++)
                    energy[y * k_Size + x] += sign * kernel[((y - py + k_Size) % k_Size) * k_Size + (x - px + k_Size) % k_Size];
            }
            int Tightest(bool value) // highest energy among pixels equal to value
            {
                int best = -1;
                for (int p = 0; p < n; p++)
                    if (ones[p] == value && (best < 0 || energy[p] > energy[best])) best = p;
                return best;
            }
            int Loosest(bool value) // lowest energy among pixels equal to value
            {
                int best = -1;
                for (int p = 0; p < n; p++)
                    if (ones[p] == value && (best < 0 || energy[p] < energy[best])) best = p;
                return best;
            }

            // 1. Random initial pattern with 10% ones, relaxed until no point wants to move.
            var random = new System.Random(1993);
            int initial = n / 10;
            for (int placed = 0; placed < initial;)
            {
                int p = random.Next(n);
                if (ones[p]) continue;
                Toggle(p, true);
                placed++;
            }
            for (int guard = 0; guard < 4 * n; guard++)
            {
                int cluster = Tightest(true);
                Toggle(cluster, false);
                int voidPixel = Loosest(false);
                Toggle(voidPixel, true);
                if (voidPixel == cluster)
                    break;
            }
            var prototype = (bool[])ones.Clone();
            var prototypeEnergy = (float[])energy.Clone();

            // 2. Rank the prototype's points: remove the tightest cluster first, it gets the highest rank.
            var rank = new int[n];
            for (int r = initial - 1; r >= 0; r--)
            {
                int cluster = Tightest(true);
                Toggle(cluster, false);
                rank[cluster] = r;
            }

            // 3. From the prototype, fill the largest void with the next rank until every pixel has one.
            System.Array.Copy(prototype, ones, n);
            System.Array.Copy(prototypeEnergy, energy, n);
            for (int r = initial; r < n; r++)
            {
                int voidPixel = Loosest(false);
                Toggle(voidPixel, true);
                rank[voidPixel] = r;
            }

            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[n];
            for (int p = 0; p < n; p++)
            {
                byte v = (byte)(rank[p] * 256 / n);
                pixels[p] = new Color32(v, v, v, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            // Noise must reach the shader unchanged: linear, unfiltered, uncompressed, no mipmaps, tiling.
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
