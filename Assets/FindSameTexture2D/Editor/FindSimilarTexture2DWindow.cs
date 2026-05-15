using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Zitga.FindSameTexture2D;

namespace Zitga.FindSameTexture2D.Editor
{
    public class FindSimilarTexture2DWindow : OdinEditorWindow
    {
        private static readonly string[] IMAGE_EXTS = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tiff", ".webp" };

        [MenuItem("Tools/Zitga/Find Similar Texture2D")]
        public static void Open()
        {
            var w = GetWindow<FindSimilarTexture2DWindow>();
            w.titleContent = new GUIContent("Find Similar Texture2D", EditorGUIUtility.IconContent("d_Texture Icon").image);
            w.minSize = new Vector2(560, 720);
            w.Show();
        }

        // ── Input Mode ───────────────────────────────────────────────────────────

        [PropertyOrder(1)]
        [Title("🔍 Find Similar Texture2D", "pHash · HSV Histogram · SSIM · Copyright © 2026 [3HP Zitga]", TitleAlignments.Left)]
        [BoxGroup("Input", ShowLabel = false)]
        [EnumToggleButtons, LabelText("Nguồn ảnh tham chiếu")]
        [OnValueChanged(nameof(OnModeChanged))]
        public InputMode Mode = InputMode.ProjectAsset;

        // ── Mode A: Project Asset ────────────────────────────────────────────────

        [PropertyOrder(2)]
        [BoxGroup("Input", ShowLabel = false)]
        [ShowIf(nameof(IsProjectMode))]
        [PreviewField(130, ObjectFieldAlignment.Left), LabelText("Texture2D (kéo từ Project)")]
        [OnValueChanged(nameof(OnInputChanged))]
        public Texture2D InputTexture;

        // ── Mode B: External File ────────────────────────────────────────────────

        [PropertyOrder(3)]
        [BoxGroup("Input", ShowLabel = false)]
        [ShowIf(nameof(IsExternalMode))]
        [LabelText("Đường dẫn ảnh"), OnValueChanged(nameof(LoadExternalImage))]
        public string ExternalImagePath;

        [PropertyOrder(4)]
        [BoxGroup("Input", ShowLabel = false)]
        [ShowIf(nameof(IsExternalMode))]
        [HorizontalGroup("Input/ExtBtns")]
        [Button("📂 Browse...", ButtonSizes.Medium)]
        private void BrowseFile()
        {
            string p = EditorUtility.OpenFilePanel("Chọn ảnh tham chiếu", "", "png,jpg,jpeg,bmp,tga,tiff,webp");
            if (!string.IsNullOrEmpty(p)) { ExternalImagePath = p; LoadExternalImage(); }
        }

        [PropertyOrder(5)]
        [HorizontalGroup("Input/ExtBtns")]
        [ShowIf(nameof(IsExternalMode))]
        [Button("🖼️ Paste Image  (Ctrl+V)", ButtonSizes.Medium)]
        private void PasteImageFromClipboard()
        {
            if (!ClipboardImageHelper.HasImage())
            {
                EditorUtility.DisplayDialog("Clipboard trống",
                    "Không tìm thấy ảnh trong clipboard.\n\nHãy chụp màn hình (Win+Shift+S / PrintScreen) hoặc\n'Copy Image' từ browser trước khi paste.", "OK");
                return;
            }
            var tex = ClipboardImageHelper.GetTexture();
            if (tex == null) { Debug.LogError("[FindSimilarTexture2D] Đọc clipboard thất bại."); return; }
            if (_externalTex != null) DestroyImmediate(_externalTex);
            _externalTex       = tex;
            ExternalPreview    = tex;
            ExternalImagePath  = ""; // không có file path vì từ clipboard
            _inputSig          = null;
            Results.Clear();
            Repaint();
        }

        [PropertyOrder(6)]
        [BoxGroup("Input", ShowLabel = false)]
        [ShowIf(nameof(HasExternalPreview))]
        [PreviewField(130, ObjectFieldAlignment.Left), LabelText("Preview"), ReadOnly]
        public Texture2D ExternalPreview;

        [PropertyOrder(7)]
        [BoxGroup("Input", ShowLabel = false)]
        [ShowIf(nameof(HasExternalPreview))]
        [DisplayAsString, LabelText("Nguồn ảnh")]
        public string ExternalInfo => ExternalPreview != null
            ? (string.IsNullOrEmpty(ExternalImagePath)
                ? $"📋 Clipboard  —  {ExternalPreview.width} × {ExternalPreview.height} px"
                : $"📁 {Path.GetFileName(ExternalImagePath)}  —  {ExternalPreview.width} × {ExternalPreview.height} px")
            : "";
        
        // ── Weights ──────────────────────────────────────────────────────────────

        [PropertyOrder(8)]
        [FoldoutGroup("⚙️ Trọng số & Cài đặt")]
        [InfoBox("Mặc định tối ưu cho UI Sprite/Icon. Tổng 3 weight nên = 1.0")]
        [Range(0f, 1f), LabelText("pHash weight (cấu trúc)")]
        [OnValueChanged(nameof(RecomputeResults))]
        public float WPHash = 0.35f;

        [PropertyOrder(9)]
        [FoldoutGroup("⚙️ Trọng số & Cài đặt")]
        [Range(0f, 1f), LabelText("HSV weight (màu sắc)")]
        [OnValueChanged(nameof(RecomputeResults))]
        public float WHsv = 0.40f;
        
        [PropertyOrder(10)]
        [FoldoutGroup("⚙️ Trọng số & Cài đặt")]
        [Range(0f, 1f), LabelText("SSIM weight (cấu trúc pixel)")]
        [OnValueChanged(nameof(RecomputeResults))]
        public float WSsim = 0.25f;

        [PropertyOrder(11)]
        [FoldoutGroup("⚙️ Trọng số & Cài đặt")]
        [Range(0f, 1f), LabelText("Ngưỡng tối thiểu")]
        [OnValueChanged(nameof(RecomputeResults))]
        public float MinScore = 0.55f;

        [PropertyOrder(12)]
        [FoldoutGroup("⚙️ Trọng số & Cài đặt")]
        [Range(1, 200), LabelText("Số kết quả tối đa")]
        [OnValueChanged(nameof(RecomputeResults))]
        public int MaxResults = 50;

        [PropertyOrder(13)]
        [FoldoutGroup("⚙️ Trọng số & Cài đặt")]
        [Button("🔄 Reset mặc định")]
        private void ResetWeights() { WPHash = 0.35f; WHsv = 0.40f; WSsim = 0.25f; RecomputeResults(); }

        // ── Sort ─────────────────────────────────────────────────────────────────

        [PropertyOrder(14)]
        [BoxGroup("Sort", ShowLabel = false)]
        [EnumToggleButtons, LabelText("Sắp xếp theo")]
        [OnValueChanged(nameof(RecomputeResults))]
        public SortMode SortBy = SortMode.Similarity;
        
        // ── Search Folder ────────────────────────────────────────────────────────

        [PropertyOrder(15)]
        [BoxGroup("Input", ShowLabel = false)]
        [FolderPath(RequireExistingPath = true), LabelText("Thư mục tìm kiếm")]
        [InfoBox("Để trống = toàn project")]
        public string SearchFolder = "Assets";

        // ── State ────────────────────────────────────────────────────────────────

        [NonSerialized] private TextureSignature _inputSig;
        [NonSerialized] private Texture2D        _externalTex;
        [NonSerialized] private bool             _isSearching;
        [NonSerialized] private float            _progress;
        [NonSerialized] private string           _progressLabel = "";
        [NonSerialized] private IEnumerator      _enumerator;
        [NonSerialized] private List<(string Path, TextureSignature Sig, Texture2D Tex)> _allScannedItems = new();

        protected override void OnEnable()
        {
            base.OnEnable();
            WPHash = EditorPrefs.GetFloat("Zitga_FST_WPHash", 0.35f);
            WHsv = EditorPrefs.GetFloat("Zitga_FST_WHsv", 0.40f);
            WSsim = EditorPrefs.GetFloat("Zitga_FST_WSsim", 0.25f);
            MinScore = EditorPrefs.GetFloat("Zitga_FST_MinScore", 0.55f);
            MaxResults = EditorPrefs.GetInt("Zitga_FST_MaxResults", 50);
            SearchFolder = EditorPrefs.GetString("Zitga_FST_SearchFolder", "Assets");
            Mode = (InputMode)EditorPrefs.GetInt("Zitga_FST_Mode", 0);
            TextureSignatureCache.LoadCache();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorPrefs.SetFloat("Zitga_FST_WPHash", WPHash);
            EditorPrefs.SetFloat("Zitga_FST_WHsv", WHsv);
            EditorPrefs.SetFloat("Zitga_FST_WSsim", WSsim);
            EditorPrefs.SetFloat("Zitga_FST_MinScore", MinScore);
            EditorPrefs.SetInt("Zitga_FST_MaxResults", MaxResults);
            EditorPrefs.SetString("Zitga_FST_SearchFolder", SearchFolder);
            EditorPrefs.SetInt("Zitga_FST_Mode", (int)Mode);
            TextureSignatureCache.SaveCache();
        }

        // ── Buttons ──────────────────────────────────────────────────────────────

        [PropertyOrder(16)]
        [BoxGroup("Input", ShowLabel = false)]
        [HorizontalGroup("Input/SearchGrp")]
        [Button("🔍  SEARCH", ButtonSizes.Large), GUIColor(0.25f, 0.78f, 0.38f)]
        [HideIf(nameof(_isSearching))]
        [EnableIf(nameof(CanSearch))]
        private void StartSearch()
        {
            var active = GetActiveTexture();
            if (active == null)
            {
                EditorUtility.DisplayDialog("Thiếu input",
                    IsProjectMode ? "Kéo Texture2D từ Project vào ô Input."
                                  : "Chọn hoặc nhập đường dẫn ảnh hợp lệ.", "OK");
                return;
            }
            Results.Clear();
            _isSearching = true;
            _enumerator  = RunSearch(active);
            EditorApplication.update += Tick;
        }

        [PropertyOrder(16.5f)]
        [BoxGroup("Input", ShowLabel = false)]
        [HorizontalGroup("Input/SearchGrp")]
        [Button("🛑 CANCEL", ButtonSizes.Large), GUIColor(0.8f, 0.3f, 0.3f)]
        [ShowIf(nameof(_isSearching))]
        private void CancelSearch()
        {
            _isSearching = false;
        }

        // ── Results ──────────────────────────────────────────────────────────────

        [PropertyOrder(17)]
        [HideInInspector]
        public List<ResultItem> Results = new();

        [PropertyOrder(18)]
        [OnInspectorGUI]
        [ShowIf(nameof(HasResults))]
        [BoxGroup("Results", ShowLabel = false)]
        private void DrawResultsGrid()
        {
            GUILayout.Space(5);
            GUILayout.Label(ResultTitle(), EditorStyles.boldLabel);
            GUILayout.Space(5);

            float width = position.width - 30; // Trừ đi padding
            float itemSize = 120;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / (itemSize + 5)));
            
            int count = Results.Count;
            int rows = Mathf.CeilToInt((float)count / columns);

            for (int r = 0; r < rows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < columns; c++)
                {
                    int index = r * columns + c;
                    if (index < count)
                    {
                        DrawItem(Results[index], itemSize);
                    }
                    else
                    {
                        GUILayout.Space(itemSize + 5);
                    }
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
        }

        private void DrawItem(ResultItem item, float size)
        {
            var rect = GUILayoutUtility.GetRect(size, size);
            var contentRect = new Rect(rect.x + 2, rect.y + 2, size - 4, size - 4);

            // Click check
            bool isClicked = Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition);
            bool isRightClicked = Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition);

            // Highlight if selected
            bool isSelected = Selection.activeObject != null && AssetDatabase.GetAssetPath(Selection.activeObject) == item.AssetPath;
            
            // Draw Background
            GUI.Box(rect, GUIContent.none, isSelected ? "SelectionRect" : EditorStyles.helpBox);

            // Draw Texture
            if (item.Texture != null)
            {
                var texRect = new Rect(contentRect.x + 5, contentRect.y + 5, contentRect.width - 10, contentRect.height - 30);
                GUI.DrawTexture(texRect, item.Texture, ScaleMode.ScaleToFit);
            }

            // Draw Score Bar
            float scorePerc = Mathf.Clamp01(item.Score.Combined);
            var scoreRect = new Rect(contentRect.x + 10, contentRect.y + contentRect.height - 22, contentRect.width - 20, 4);
            EditorGUI.DrawRect(scoreRect, new Color(0.2f, 0.2f, 0.2f, 0.5f)); // bg
            var fillRect = new Rect(scoreRect.x, scoreRect.y, scoreRect.width * scorePerc, scoreRect.height);
            Color barColor = Color.Lerp(Color.red, Color.green, scorePerc);
            EditorGUI.DrawRect(fillRect, barColor);

            // Draw Name
            var nameRect = new Rect(contentRect.x + 5, contentRect.y + contentRect.height - 16, contentRect.width - 10, 16);
            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GUI.Label(nameRect, Path.GetFileName(item.AssetPath), style);

            // Tooltip & Hover effect
            GUI.Label(rect, new GUIContent("", $"{Path.GetFileName(item.AssetPath)}\n{item.AssetPath}\n\n" +
                                               $"pHash: {item.Score.PHashScore:F2}\n" +
                                               $"HSV: {item.Score.HSVScore:F2}\n" +
                                               $"SSIM: {item.Score.SSIMScore:F2}"));

            if (isClicked)
            {
                if (Event.current.clickCount == 2)
                {
                    // Mở bằng phần mềm xem ảnh mặc định
                    EditorUtility.OpenWithDefaultApp(Path.GetFullPath(item.AssetPath));
                }
                else
                {
                    PingAsset(item.AssetPath);
                }
                Event.current.Use();
            }

            if (isRightClicked)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Ping Asset"), false, () => PingAsset(item.AssetPath));
                menu.AddItem(new GUIContent("Copy Path"), false, () => { GUIUtility.systemCopyBuffer = item.AssetPath; });
                menu.AddItem(new GUIContent("Show in Explorer"), false, () => EditorUtility.RevealInFinder(item.AssetPath));
                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        private void PingAsset(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        [PropertyOrder(19)]
        [Button("🧹 Xóa kết quả"), GUIColor(0.8f, 0.35f, 0.3f), ShowIf(nameof(HasResults))]
        [BoxGroup("Results", ShowLabel = false)]
        private void ClearResults() => Results.Clear();

        // ── Logic ────────────────────────────────────────────────────────────────

        private bool IsProjectMode  => Mode == InputMode.ProjectAsset;
        private bool IsExternalMode => Mode == InputMode.ExternalFile;
        private bool HasExternalPreview => IsExternalMode && ExternalPreview != null;
        private bool HasResults     => Results != null && Results.Count > 0;
        private bool CanSearch      => GetActiveTexture() != null && !_isSearching;

        private Texture2D GetActiveTexture() =>
            IsProjectMode ? InputTexture : _externalTex;

        private void OnModeChanged() { Results.Clear(); _inputSig = null; }
        private void OnInputChanged() { Results.Clear(); _inputSig = null; }

        private void LoadExternalImage()
        {
            if (string.IsNullOrEmpty(ExternalImagePath) || !File.Exists(ExternalImagePath)) return;
            try
            {
                byte[] data = File.ReadAllBytes(ExternalImagePath);
                if (_externalTex != null) DestroyImmediate(_externalTex);
                _externalTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _externalTex.LoadImage(data);
                _externalTex.Apply();
                ExternalPreview = _externalTex;
                _inputSig = null;
                Results.Clear();
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[FindSimilarTexture2D] Không đọc được file: {e.Message}");
            }
        }

        // ── Search coroutine ─────────────────────────────────────────────────────

        private void Tick()
        {
            if (_enumerator == null || !_enumerator.MoveNext())
            {
                EditorApplication.update -= Tick;
                _isSearching = false;
                EditorUtility.ClearProgressBar();
                _enumerator = null;
                Repaint();
            }
        }

        private void RecomputeResults()
        {
            if (_allScannedItems == null || _allScannedItems.Count == 0 || _inputSig == null) return;

            var found = new List<ResultItem>();
            foreach (var item in _allScannedItems)
            {
                var sc = TextureSimilarity.Compare(_inputSig, item.Sig, WPHash, WHsv, WSsim);
                if (sc.Combined < MinScore) continue;
                found.Add(new ResultItem { Texture = item.Tex, AssetPath = item.Path, Score = sc });
            }

            Results = (SortBy == SortMode.Similarity
                ? found.OrderByDescending(r => r.Score.Combined)
                : found.OrderBy(r => Path.GetFileName(r.AssetPath)))
                .Take(MaxResults).ToList();
            
            Repaint();
        }

        private IEnumerator RunSearch(Texture2D inputTex)
        {
            UpdateProg("Đang xử lý ảnh input...", 0f);
            yield return null;

            try { _inputSig = TextureSimilarity.ComputeSignature(inputTex); }
            catch (Exception e) { Debug.LogError($"[FindSimilarTexture2D] {e.Message}"); yield break; }

            UpdateProg("Đang quét project...", 0.02f);
            yield return null;

            string folder = string.IsNullOrWhiteSpace(SearchFolder) ? "Assets" : SearchFolder;
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            string inputPath = IsProjectMode ? AssetDatabase.GetAssetPath(InputTexture) : "";
            int total = guids.Length;
            _allScannedItems.Clear();

            for (int i = 0; i < total; i++)
            {
                if (!_isSearching) yield break;

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path == inputPath) continue;

                UpdateProg($"[{i + 1}/{total}] {Path.GetFileName(path)}", 0.02f + 0.95f * i / total);
                // Với số lượng file lớn và dùng cache, việc yield mỗi 4 frame quá chậm, tăng lên 25
                if (i % 25 == 0) yield return null;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                TextureSignature sig;
                try { sig = TextureSignatureCache.GetSignature(tex, path, guids[i]); } catch { continue; }

                _allScannedItems.Add((path, sig, tex));
            }

            TextureSignatureCache.SaveCache();

            UpdateProg("Đang tổng hợp...", 0.99f);
            yield return null;

            RecomputeResults();
        }

        // ── Drag & Drop từ Explorer (external file) ──────────────────────────────

        protected override void OnImGUI()
        {
            // Ctrl+V → paste image from clipboard (chỉ khi ở External mode)
            var ev = Event.current;
            if (IsExternalMode &&
                ev.type    == EventType.KeyDown &&
                ev.keyCode == KeyCode.V &&
                ev.control)
            {
                PasteImageFromClipboard();
                ev.Use();
            }

            // Drag & drop zone khi ở External mode
            if (IsExternalMode)
            {
                if (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                {
                    // Lấy path: DragAndDrop.paths chứa absolute path khi kéo từ Explorer
                    string droppedPath = GetDroppedImagePath();
                    if (droppedPath != null)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        if (ev.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            ExternalImagePath = droppedPath;
                            LoadExternalImage();
                        }
                        ev.Use();
                    }
                }
            }

            base.OnImGUI();

            // Progress bar inline
            if (_isSearching)
            {
                var r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(r, _progress, _progressLabel);
                Repaint();
            }

            // Drop zone hint khi external mode chưa có ảnh
            if (IsExternalMode && _externalTex == null)
            {
                var rect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
                rect = new RectOffset(10, 10, 4, 4).Remove(rect);
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 0.4f));
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    { fontSize = 12, wordWrap = true };
                GUI.Label(rect, "🖼️  Kéo ảnh PNG/JPG từ Explorer vào đây\nhoặc dùng nút Browse / Paste Path phía trên", style);
                Repaint();
            }
        }

        /// <summary>
        /// Lấy path ảnh hợp lệ từ DragAndDrop.
        /// Kéo từ Explorer → DragAndDrop.paths chứa absolute path hệ điều hành.
        /// Kéo từ Project window → paths chứa Assets/... relative path (không dùng ở External mode).
        /// </summary>
        private static string GetDroppedImagePath()
        {
            var paths = DragAndDrop.paths;
            if (paths == null || paths.Length == 0) return null;
            string p = paths[0];
            // Absolute OS path (từ Explorer)
            if (Path.IsPathRooted(p) && File.Exists(p) && IsImageFile(p)) return p;
            // Relative Unity path → convert sang absolute
            string abs = Path.GetFullPath(p);
            if (File.Exists(abs) && IsImageFile(abs)) return abs;
            return null;
        }

        private void UpdateProg(string label, float p)
        {
            _progressLabel = label; _progress = p;
            EditorUtility.DisplayProgressBar("Find Similar Texture2D", label, p);
        }

        private static bool IsImageFile(string path) =>
            IMAGE_EXTS.Contains(Path.GetExtension(path).ToLowerInvariant());

        private string ResultTitle() => HasResults ? $"Kết quả: {Results.Count} texture tương tự" : "Kết quả";
    }

    // ── Enums ────────────────────────────────────────────────────────────────────

    public enum InputMode { ProjectAsset, ExternalFile }
    public enum SortMode  { Similarity, Name }

    // ── Result Item ──────────────────────────────────────────────────────────────

    [Serializable, HideReferenceObjectPicker]
    public class ResultItem
    {
        public string            AssetPath;
        public TextureMatchScore Score;
        public Texture2D         Texture;
    }

    // ── Cache ──────────────────────────────────────────────────────────────────

    [Serializable]
    public class TextureSignatureCacheData
    {
        public string Guid;
        public string Hash;
        public string PHash;
        public float[] HSVHistogram;
        public float[] GrayPixels;
    }

    [Serializable]
    public class TextureSignatureCacheFile
    {
        public List<TextureSignatureCacheData> Entries = new List<TextureSignatureCacheData>();
    }

    public static class TextureSignatureCache
    {
        private static readonly string CachePath = "Library/FindSameTexture2DCache.json";
        private static Dictionary<string, TextureSignatureCacheData> _cacheMap = new Dictionary<string, TextureSignatureCacheData>();
        private static bool _isLoaded = false;
        private static bool _isDirty = false;

        public static void LoadCache()
        {
            if (_isLoaded) return;
            _cacheMap.Clear();
            if (File.Exists(CachePath))
            {
                try
                {
                    string json = File.ReadAllText(CachePath);
                    var fileData = JsonUtility.FromJson<TextureSignatureCacheFile>(json);
                    if (fileData != null && fileData.Entries != null)
                    {
                        foreach (var entry in fileData.Entries)
                        {
                            _cacheMap[entry.Guid] = entry;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FindSameTexture2D] Lỗi đọc cache: {e.Message}");
                }
            }
            _isLoaded = true;
        }

        public static void SaveCache()
        {
            if (!_isDirty) return;
            try
            {
                var fileData = new TextureSignatureCacheFile();
                foreach (var kvp in _cacheMap)
                {
                    fileData.Entries.Add(kvp.Value);
                }
                string json = JsonUtility.ToJson(fileData);
                File.WriteAllText(CachePath, json);
                _isDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FindSameTexture2D] Lỗi lưu cache: {e.Message}");
            }
        }

        public static TextureSignature GetSignature(Texture2D tex, string path, string guid)
        {
            LoadCache();
            string fileHash = AssetDatabase.GetAssetDependencyHash(path).ToString();

            if (_cacheMap.TryGetValue(guid, out var cachedData))
            {
                if (cachedData.Hash == fileHash)
                {
                    return new TextureSignature
                    {
                        PHash = StringToBoolArray(cachedData.PHash),
                        HSVHistogram = cachedData.HSVHistogram,
                        GrayPixels = cachedData.GrayPixels
                    };
                }
            }

            var newSig = TextureSimilarity.ComputeSignature(tex);

            _cacheMap[guid] = new TextureSignatureCacheData
            {
                Guid = guid,
                Hash = fileHash,
                PHash = BoolArrayToString(newSig.PHash),
                HSVHistogram = newSig.HSVHistogram,
                GrayPixels = newSig.GrayPixels
            };
            _isDirty = true;

            return newSig;
        }

        private static string BoolArrayToString(bool[] arr)
        {
            if (arr == null) return "";
            char[] chars = new char[arr.Length];
            for (int i = 0; i < arr.Length; i++) chars[i] = arr[i] ? '1' : '0';
            return new string(chars);
        }

        private static bool[] StringToBoolArray(string s)
        {
            if (string.IsNullOrEmpty(s)) return new bool[0];
            bool[] arr = new bool[s.Length];
            for (int i = 0; i < s.Length; i++) arr[i] = s[i] == '1';
            return arr;
        }
    }
}
