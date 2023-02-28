using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class AtlasPacker : EditorWindow
{
	[MenuItem("Window/Textures/Atlas Packer")]
	public static void ShowWindow()
	{
		EditorWindow window = GetWindow(typeof(AtlasPacker), false, "Atlas Packer");
		window.minSize = new(300, 300);

		window.Show();
	}

	private static GUIStyle s_redLabelStyle;

	private FilterMode _filterMode = FilterMode.Bilinear;
	private TextureImporterType _textureImporterType;
	private Vector2 _spritePivot = new(0.5f, 0.5f);
	private Vector4 _spriteBorder;
	private bool _alphaIsTransparency;
	private bool _generateSprites = true;
	private bool _keepReadable;
	private bool _markReadable = true;
	private float _scale = 1f;
	private float _textureArea;
	private int _atlasSize;
	private int _nonReadableCount;
	private readonly List<Texture2D> _textures = new();
	private string _folderPath;
	private string _outputName = "Atlas";
	private string _outputPath;

	private void OnGUI()
	{
		// Directory selection.
		bool hasInput = !string.IsNullOrEmpty(_folderPath);
		string root = Application.dataPath;

		// Input.
		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.BeginVertical();
		EditorGUILayout.LabelField("Input", EditorStyles.miniLabel);

		if (GUILayout.Button(hasInput ? "...\\" + Path.GetRelativePath(root, _folderPath) : "Select folder"))
		{
			// Open folder selection dialog.
			_folderPath = EditorUtility.OpenFolderPanel("Select folder", root, "");

			// Check if the path is valid.
			if (string.IsNullOrEmpty(_folderPath) || !Directory.Exists(_folderPath))
			{
				_folderPath = null;
				hasInput = false;
			}
			else
			{
				DirectoryInfo info = new(_folderPath);
				_outputPath = info.Parent?.FullName;
				_outputName = info.Name + "Atlas.png";
				UpdateInput();
			}
		}

		EditorGUILayout.EndVertical();

		// Output.
		EditorGUILayout.BeginVertical();
		EditorGUILayout.LabelField("Output", EditorStyles.miniLabel);
		bool hasOutput = !string.IsNullOrEmpty(_outputPath);

		if (GUILayout.Button(hasOutput ? "...\\" + Path.GetRelativePath(root, _outputPath) : "Select folder"))
		{
			// Open folder selection dialog.
			_outputPath = EditorUtility.OpenFolderPanel("Select folder", root, "");

			// Check if the path is valid.
			if (string.IsNullOrEmpty(_outputPath) || !Directory.Exists(_outputPath))
			{
				_outputPath = null;
				hasOutput = false;
			}
		}

		if (hasOutput)
			_outputName = GUILayout.TextField(_outputName);

		EditorGUILayout.EndVertical();
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();

		// Statistics.
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);

		int textureCount = _textures.Count;

		if (textureCount > 0)
		{
			EditorGUILayout.LabelField($"{textureCount} textures", EditorStyles.miniLabel);
			EditorGUILayout.LabelField($"{_textureArea} total pixels", EditorStyles.miniLabel);
			EditorGUILayout.LabelField($"{_atlasSize}x{_atlasSize} atlas size", EditorStyles.miniLabel);

			if (_nonReadableCount > 0)
			{
				s_redLabelStyle ??= new(EditorStyles.miniLabel) { normal = { textColor = new(1f, 0.2f, 0.2f) } };
				EditorGUILayout.LabelField($"{_nonReadableCount} non-readable textures", s_redLabelStyle);
			}
		}
		else
		{
			EditorGUILayout.LabelField("No textures found...", EditorStyles.boldLabel);
		}

		if (hasInput && GUILayout.Button("Refresh", EditorStyles.miniButton))
			UpdateInput();

		EditorGUILayout.EndVertical();

		// Options.
		if (textureCount == 0) return;

		// Input.
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);

		_generateSprites = EditorGUILayout.Toggle("Generate sprites", _generateSprites);
		_markReadable = EditorGUILayout.Toggle("Mark readable", _markReadable);
		_keepReadable = EditorGUILayout.Toggle("Keep readable", _keepReadable);

		// Output.
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

		// Scale.
		EditorGUILayout.BeginHorizontal();

		_scale = EditorGUILayout.Slider("Scale", _scale, 0f, 1f);

		if (GUILayout.Button("Reset", GUILayout.Width(80f)))
			_scale = 1f;

		EditorGUILayout.EndHorizontal();

		// Resolution.
		EditorGUILayout.BeginHorizontal();
		EditorGUI.BeginChangeCheck();

		int targetResolution = EditorGUILayout.IntField("Resolution", (int)(_atlasSize * _scale));

		if (GUILayout.Button("²", GUILayout.Width(80f)))
			targetResolution = Mathf.ClosestPowerOfTwo(targetResolution) + 1;

		if (EditorGUI.EndChangeCheck())
			_scale = Mathf.Clamp01(targetResolution / (float)_atlasSize);

		EditorGUILayout.EndHorizontal();

		// Texture options.
		_alphaIsTransparency = EditorGUILayout.Toggle("Alpha is transparency", _alphaIsTransparency);
		_filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter mode", _filterMode);

		// Sprite options.
		if (_generateSprites)
		{
			_spritePivot = EditorGUILayout.Vector2Field("Sprite pivot", _spritePivot);
			_spriteBorder = EditorGUILayout.Vector4Field("Sprite border", _spriteBorder);
		}
		else
		{
			_textureImporterType = (TextureImporterType)EditorGUILayout.EnumPopup("Texture type", _textureImporterType);
		}

		// Output.
		EditorGUILayout.Space();

		if (GUILayout.Button("Build Atlas"))
			Build();
	}

	private void UpdateInput()
	{
		string root = Application.dataPath;

		if (string.IsNullOrWhiteSpace(_folderPath)) return;

		// Get all files from path.
		string[] files = Directory.GetFiles(_folderPath);

		// Loop through files and load texture.
		_textures.Clear();
		_textureArea = 0f;
		_atlasSize = 0;

		foreach (string file in files)
		{
			string name = "Assets/" + Path.GetRelativePath(root, file);

			if (AssetDatabase.LoadAssetAtPath(name, typeof(Texture2D)) is not Texture2D texture) continue;

			_textures.Add(texture);

			_textureArea += texture.width * (float)texture.height;
		}

		_nonReadableCount = _textures.Count(x => !x.isReadable);

		if (_textures.Count == 0)
		{
			EditorUtility.DisplayDialog("No textures found",
				"Please select a folder that contains textures. Nesting not supported.", "OK");
			return;
		}

		// Sort textures by height.
		_textures.Sort((a, b) => a.height.CompareTo(b.height));

		// Create atlas.
		_atlasSize = Mathf.CeilToInt(Mathf.Sqrt(_textureArea));
		int gridCount = Mathf.CeilToInt(Mathf.Sqrt(_textures.Count));
		int gridSize = _atlasSize / gridCount;

		_atlasSize += gridSize;
	}

	private void Build()
	{
		string root = Application.dataPath;
		int atlasSize = (int)(_atlasSize * _scale);
		
		// Create atlas.
		Texture2D atlas = new(atlasSize, atlasSize, TextureFormat.ARGB32, false)
		{
			alphaIsTransparency = _alphaIsTransparency,
			filterMode = _filterMode,
		};

		SpriteMetaData[] sprites = _generateSprites ? new SpriteMetaData[_textures.Count] : null;
		
		// Mark textures as readable.
		List<(TextureImporter importer, string path)> previouslyNonReadable = new();
		
		if (_markReadable)
		{
			foreach (Texture2D texture in _textures)
			{
				// Only update readable textures.
				if (texture.isReadable) continue;
				
				// Get texture importer.
				string textureAssetPath = AssetDatabase.GetAssetPath(texture);
				if (AssetImporter.GetAtPath(textureAssetPath) is not TextureImporter textureImporter) continue;
				
				// Update importer.
				textureImporter.isReadable = true;
				previouslyNonReadable.Add((textureImporter, textureAssetPath));
				AssetDatabase.ImportAsset(textureAssetPath);
			}
		}

		// Generate atlas.
		for (int j = 0; j < atlas.width; j++)
		for (int k = 0; k < atlas.height; k++)
			atlas.SetPixel(j, k, Color.clear);

		int x = 0;
		int y = 0;
		int height = 0;

		for (int i = 0; i < _textures.Count; i++)
		{
			Texture2D texture = _textures[i];
			int textureWidth = (int)(texture.width * _scale);
			int textureHeight = (int)(texture.height * _scale);

			if (x + textureWidth >= atlasSize)
			{
				x = 0;
				y += height;
				height = 0;
			}

			if (sprites != null)
			{
				sprites[i] = new()
				{
					name = texture.name,
					rect = new(x, y, textureWidth, textureHeight),
					pivot = _spritePivot,
					border = _spriteBorder,
				};
			}

			for (int j = 0; j < texture.width; j++)
			{
				for (int k = 0; k < texture.height; k++)
				{
					Color color = texture.GetPixel(j, k);
					atlas.SetPixel(x + (int)(j * _scale), y + (int)(k * _scale), color);
				}
			}

			x += textureWidth;
			height = Mathf.Max(height, textureHeight);
		}
		
		// Apply texture.
		atlas.Apply();
		
		// Save atlas.
		string atlasPath = Path.GetRelativePath(root, $"{_outputPath}/{_outputName}");
		string atlasAssetPath = "Assets/" + atlasPath;

		EditorUtility.DisplayDialog("Created atlas texture", $"Atlas texture created at {atlasPath}!", "OK");

		File.WriteAllBytes($"{Application.dataPath}/{atlasPath}", atlas.EncodeToPNG());

		AssetDatabase.Refresh();
		AssetDatabase.ImportAsset(atlasAssetPath);

		if (AssetImporter.GetAtPath(atlasAssetPath) is not TextureImporter atlasImporter) return;

		if (_generateSprites)
		{
			atlasImporter.textureType = TextureImporterType.Sprite;
			atlasImporter.spriteImportMode = SpriteImportMode.Multiple;
			atlasImporter.spritesheet = sprites;
		}
		else
		{
			atlasImporter.textureType = _textureImporterType;
		}
		
		atlasImporter.alphaIsTransparency = _alphaIsTransparency;
		atlasImporter.filterMode = _filterMode;
		
		AssetDatabase.ImportAsset(atlasAssetPath);
		
		// Reset textures to non-readable.
		if (!_keepReadable)
		{
			foreach ((TextureImporter importer, string path) nonReadable in previouslyNonReadable)
			{
				nonReadable.importer.isReadable = false;
				AssetDatabase.ImportAsset(nonReadable.path);
			}
		}

		AssetDatabase.Refresh();
	}
}