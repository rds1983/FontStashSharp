using FontStashSharp.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;

#if MONOGAME || FNA
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
#elif STRIDE
using Stride.Core.Mathematics;
using Stride.Graphics;
using Texture2D = Stride.Graphics.Texture;
#else
using System.Drawing;
using Texture2D = System.Object;
#endif

namespace FontStashSharp
{
	public class FontSystem : IDisposable
	{
		private readonly List<IFontSource> _fontSources = new List<IFontSource>();
		private readonly Int32Map<DynamicSpriteFont> _fonts = new Int32Map<DynamicSpriteFont>();
		private readonly FontSystemSettings _settings;

		private readonly IFontLoader _fontLoader;

		private FontAtlas _currentAtlas;

		public FontSystemEffect Effect => _settings.Effect;
		public int EffectAmount => _settings.EffectAmount;

		public int TextureWidth => _settings.TextureWidth;
		public int TextureHeight => _settings.TextureHeight;

		public bool PremultiplyAlpha => _settings.PremultiplyAlpha;

		public float FontResolutionFactor => _settings.FontResolutionFactor;

		public int KernelWidth => _settings.KernelWidth;
		public int KernelHeight => _settings.KernelHeight;

		public Texture2D ExistingTexture => _settings.ExistingTexture;
		public Rectangle ExistingTextureUsedSpace => _settings.ExistingTextureUsedSpace;

		public bool UseKernings = true;
		public int? DefaultCharacter = ' ';

		public int CharacterSpacing = 0;
		public int LineSpacing = 0;

		internal int BlurAmount => Effect == FontSystemEffect.Blurry ? EffectAmount : 0;
		internal int StrokeAmount => Effect == FontSystemEffect.Stroked ? EffectAmount : 0;

		public List<FontAtlas> Atlases { get; } = new List<FontAtlas>();

		public event EventHandler CurrentAtlasFull;

		public FontSystem(IFontLoader fontLoader, FontSystemSettings settings)
		{
			if (fontLoader == null)
			{
				throw new ArgumentNullException(nameof(fontLoader));
			}

			if (settings == null)
			{
				throw new ArgumentNullException(nameof(settings));
			}

			_fontLoader = fontLoader;
			_settings = settings.Clone();
		}

		public FontSystem(FontSystemSettings settings) : this(StbTrueTypeSharpFontLoader.Instance, settings)
		{
		}

		public FontSystem() : this(FontSystemSettings.Default)
		{
		}

		public void Dispose()
		{
			if (_fontSources != null)
			{
				foreach (var font in _fontSources)
					font.Dispose();
				_fontSources.Clear();
			}

			Atlases?.Clear();
			_currentAtlas = null;
			_fonts.Clear();
		}

		public void AddFont(byte[] data)
		{
			var fontSource = _fontLoader.Load(data);
			_fontSources.Add(fontSource);
		}

		public void AddFont(Stream stream)
		{
			AddFont(stream.ToByteArray());
		}

		public DynamicSpriteFont GetFont(int fontSize)
		{
			DynamicSpriteFont result;
			if (_fonts.TryGetValue(fontSize, out result))
			{
				return result;
			}

			result = new DynamicSpriteFont(this, fontSize);
			_fonts[fontSize] = result;
			return result;
		}

		public void Reset()
		{
			Atlases.Clear();
			_fonts.Clear();
		}

		internal int? GetCodepointIndex(int codepoint, out IFontSource font)
		{
			font = null;

			var g = default(int?);
			foreach (var f in _fontSources)
			{
				g = f.GetGlyphId(codepoint);
				if (g != null)
				{
					font = f;
					break;
				}
			}

			return g;
		}

#if MONOGAME || FNA || STRIDE
		private FontAtlas GetCurrentAtlas(GraphicsDevice device, int textureWidth, int textureHeight)
#else
		private FontAtlas GetCurrentAtlas(ITexture2DManager device, int textureWidth, int textureHeight)
#endif
		{
			if (_currentAtlas == null)
			{
				Texture2D existingTexture = null;
				if (ExistingTexture != null && Atlases.Count == 0)
				{
					existingTexture = ExistingTexture;
				}

				_currentAtlas = new FontAtlas(textureWidth, textureHeight, 256, existingTexture);

				// If existing texture is used, mark existing used rect as used
				if (existingTexture != null && !ExistingTextureUsedSpace.IsEmpty)
				{
					if (!_currentAtlas.AddSkylineLevel(0, ExistingTextureUsedSpace.X, ExistingTextureUsedSpace.Y, ExistingTextureUsedSpace.Width, ExistingTextureUsedSpace.Height))
					{
						throw new Exception(string.Format("Unable to specify existing texture used space: {0}", ExistingTextureUsedSpace));
					}

					// TODO: Clear remaining space
				}

				Atlases.Add(_currentAtlas);
			}

			return _currentAtlas;
		}

#if MONOGAME || FNA || STRIDE
		internal void RenderGlyphOnAtlas(GraphicsDevice device, DynamicFontGlyph glyph)
#else
		internal void RenderGlyphOnAtlas(ITexture2DManager device, DynamicFontGlyph glyph)
#endif
		{
			var textureSize = new Point(TextureWidth, TextureHeight);

			if (ExistingTexture != null)
			{
#if MONOGAME || FNA || STRIDE
				textureSize = new Point(ExistingTexture.Width, ExistingTexture.Height);
#else
				textureSize = device.GetTextureSize(ExistingTexture);
#endif
			}

			int gx = 0, gy = 0;
			var gw = glyph.Bounds.Width;
			var gh = glyph.Bounds.Height;

			var currentAtlas = GetCurrentAtlas(device, textureSize.X, textureSize.Y);
			if (!currentAtlas.AddRect(gw, gh, ref gx, ref gy))
			{
				CurrentAtlasFull?.Invoke(this, EventArgs.Empty);

				// This code will force creation of new atlas
				_currentAtlas = null;
				currentAtlas = GetCurrentAtlas(device, textureSize.X, textureSize.Y);

				// Try to add again
				if (!currentAtlas.AddRect(gw, gh, ref gx, ref gy))
				{
					throw new Exception(string.Format("Could not add rect to the newly created atlas. gw={0}, gh={1}", gw, gh));
				}
			}

			glyph.Bounds.X = gx;
			glyph.Bounds.Y = gy;

			currentAtlas.RenderGlyph(device, glyph, BlurAmount, StrokeAmount, PremultiplyAlpha, KernelWidth, KernelHeight);

			glyph.Texture = currentAtlas.Texture;
		}
	}
}
