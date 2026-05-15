using System;
using UnityEngine;

namespace Zitga.FindSameTexture2D
{
    /// <summary>Precomputed features của một texture — tính 1 lần, dùng nhiều lần.</summary>
    public class TextureSignature
    {
        public bool[]  PHash;        // 64-bit DCT perceptual hash
        public float[] HSVHistogram; // H:18 + S:8 + V:8 = 34 bins
        public float[] GrayPixels;   // 32x32 grayscale (dùng cho SSIM)
    }

    /// <summary>Kết quả so sánh với đủ 3 metric scores.</summary>
    public struct TextureMatchScore
    {
        public float PHashScore;
        public float HSVScore;
        public float SSIMScore;
        public float Combined;

        public TextureMatchScore(float ph, float hsv, float ssim, float wPh, float wHsv, float wSsim)
        {
            PHashScore = ph; HSVScore = hsv; SSIMScore = ssim;
            Combined   = ph * wPh + hsv * wHsv + ssim * wSsim;
        }
    }

    public static class TextureSimilarity
    {
        // ── Constants ────────────────────────────────────────────────────────────
        private const int   PHASH_N  = 32;   // resize to 32×32 for DCT
        private const int   PHASH_M  = 8;    // keep top-left 8×8 → 64-bit hash
        private const int   H_BINS   = 18;
        private const int   S_BINS   = 8;
        private const int   V_BINS   = 8;
        public  const int   HSV_BINS = H_BINS + S_BINS + V_BINS; // 34
        private const float C1       = 0.0001f; // SSIM (0.01)^2
        private const float C2       = 0.0009f; // SSIM (0.03)^2

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Precompute signature cho một texture (gọi 1 lần per texture).</summary>
        public static TextureSignature ComputeSignature(Texture2D tex)
        {
            var src    = MakeReadable(tex);
            var sm32   = ScaleTexture(src, PHASH_N, PHASH_N);
            var px32   = sm32.GetPixels();
            var sm64   = ScaleTexture(src, 64, 64);
            var px64   = sm64.GetPixels();

            float[]   gray   = ToGray(px32);
            float[,]  gray2D = ToGray2D(px32, PHASH_N);

            var sig = new TextureSignature
            {
                PHash        = BuildPHash(gray2D),
                HSVHistogram = BuildHSVHist(px64),
                GrayPixels   = gray
            };

            UnityEngine.Object.DestroyImmediate(sm32);
            UnityEngine.Object.DestroyImmediate(sm64);
            UnityEngine.Object.DestroyImmediate(src);
            return sig;
        }

        /// <summary>So sánh hai signature. Weight mặc định tối ưu cho UI Sprite/Icon.</summary>
        public static TextureMatchScore Compare(
            TextureSignature a, TextureSignature b,
            float wPHash = 0.35f, float wHSV = 0.40f, float wSSIM = 0.25f)
        {
            float ph   = PHashSim(a.PHash,        b.PHash);
            float hsv  = HSVSim  (a.HSVHistogram,  b.HSVHistogram);
            float ssim = SSIMSim (a.GrayPixels,    b.GrayPixels);
            return new TextureMatchScore(ph, hsv, ssim, wPHash, wHSV, wSSIM);
        }

        // ── Static Buffers (Tránh tạo rác bộ nhớ) ────────────────────────────────
        private static readonly float[,] _tmpDct = new float[PHASH_N, PHASH_N];
        private static readonly float[,] _resDct = new float[PHASH_N, PHASH_N];
        private static readonly float[,] _gray2D = new float[PHASH_N, PHASH_N];
        private static readonly float[] _gray1D = new float[PHASH_N * PHASH_N];

        // ── pHash (DCT-based) ────────────────────────────────────────────────────

        private static bool[] BuildPHash(float[,] gray)
        {
            int n = PHASH_N, m = PHASH_M;
            var dct = DCT2D(gray, n);

            // Collect top-left m×m minus DC[0,0]
            var vals = new float[m * m - 1];
            int idx = 0;
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                    if (y != 0 || x != 0) vals[idx++] = dct[y, x];

            var sorted = (float[])vals.Clone();
            Array.Sort(sorted);
            float med = sorted[sorted.Length / 2];

            var hash = new bool[m * m];
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                    hash[y * m + x] = dct[y, x] > med;
            return hash;
        }

        // Separable 2D DCT — O(N³)
        private static float[,] DCT2D(float[,] inp, int n)
        {
            float piN = Mathf.PI / n;

            for (int y = 0; y < n; y++)
                for (int k = 0; k < n; k++)
                {
                    float s = 0f;
                    for (int x = 0; x < n; x++) s += inp[y, x] * Mathf.Cos(piN * (x + 0.5f) * k);
                    _tmpDct[y, k] = s;
                }

            for (int kx = 0; kx < n; kx++)
                for (int ky = 0; ky < n; ky++)
                {
                    float s = 0f;
                    for (int y = 0; y < n; y++) s += _tmpDct[y, kx] * Mathf.Cos(piN * (y + 0.5f) * ky);
                    _resDct[ky, kx] = s;
                }
            return _resDct;
        }

        private static float PHashSim(bool[] a, bool[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;
            int match = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] == b[i]) match++;
            return (float)match / a.Length;
        }

        // ── HSV Histogram ────────────────────────────────────────────────────────

        private static float[] BuildHSVHist(Color[] pixels)
        {
            var hist  = new float[HSV_BINS];
            int count = 0;

            foreach (var p in pixels)
            {
                // Bỏ qua pixel trong suốt (alpha < 0.1) — quan trọng với sprite có nền trong
                if (p.a < 0.1f) continue;
                Color.RGBToHSV(p, out float h, out float s, out float v);
                hist[Mathf.Clamp((int)(h * H_BINS), 0, H_BINS - 1)]++;
                hist[H_BINS + Mathf.Clamp((int)(s * S_BINS), 0, S_BINS - 1)]++;
                hist[H_BINS + S_BINS + Mathf.Clamp((int)(v * V_BINS), 0, V_BINS - 1)]++;
                count++;
            }

            if (count == 0) return hist;

            // Normalize từng channel riêng về [0,1]
            for (int i = 0;          i < H_BINS;         i++) hist[i]                    /= count;
            for (int i = H_BINS;     i < H_BINS + S_BINS; i++) hist[i]                   /= count;
            for (int i = H_BINS + S_BINS; i < HSV_BINS;  i++) hist[i]                    /= count;
            return hist;
        }

        private static float HSVSim(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;
            // Bhattacharyya coefficient per channel — H weighted 50%, S 30%, V 20%
            float bcH = 0f, bcS = 0f, bcV = 0f;
            for (int i = 0;              i < H_BINS;  i++) bcH += Mathf.Sqrt(a[i] * b[i]);
            for (int i = H_BINS;         i < H_BINS + S_BINS; i++) bcS += Mathf.Sqrt(a[i] * b[i]);
            for (int i = H_BINS + S_BINS; i < HSV_BINS; i++) bcV += Mathf.Sqrt(a[i] * b[i]);
            return Mathf.Clamp01(bcH * 0.5f + bcS * 0.3f + bcV * 0.2f);
        }

        // ── SSIM ─────────────────────────────────────────────────────────────────

        private static float SSIMSim(float[] ga, float[] gb)
        {
            if (ga == null || gb == null || ga.Length != gb.Length) return 0f;
            int n = ga.Length;

            float mA = 0f, mB = 0f;
            for (int i = 0; i < n; i++) { mA += ga[i]; mB += gb[i]; }
            mA /= n; mB /= n;

            float vA = 0f, vB = 0f, cov = 0f;
            for (int i = 0; i < n; i++)
            {
                float da = ga[i] - mA, db = gb[i] - mB;
                vA += da * da; vB += db * db; cov += da * db;
            }
            vA /= n; vB /= n; cov /= n;

            float ssim = (2 * mA * mB + C1) * (2 * cov + C2)
                       / ((mA * mA + mB * mB + C1) * (vA + vB + C2));
            return Mathf.Clamp01((ssim + 1f) / 2f);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static float[] ToGray(Color[] px)
        {
            var g = new float[px.Length]; // Vẫn tạo mới để lưu vào TextureSignature
            for (int i = 0; i < px.Length; i++)
                g[i] = px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f;
            return g;
        }

        private static float[,] ToGray2D(Color[] px, int n)
        {
            for (int i = 0; i < px.Length; i++)
                _gray2D[i / n, i % n] = px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f;
            return _gray2D;
        }

        public static Texture2D MakeReadable(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture.active = rt;
            Graphics.Blit(src, rt);
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        public static Texture2D ScaleTexture(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            RenderTexture.active = rt;
            Graphics.Blit(src, rt);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
