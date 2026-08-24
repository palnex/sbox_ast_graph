#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox;
using SkiaSharp;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

public struct LabelAtlasMetadata
{
    public Vector4 UvBounds;  // x: uMin, y: vMin, z: uMax, w: vMax
    public Vector2 PixelSize; // width & height in physical pixels
    public bool IsValid;
}

public sealed class DynamicUnicodeAtlas : IDisposable
{
    public readonly int AtlasWidth;
    public readonly int AtlasHeight;
    public Texture GpuTexture { get; private set; }

    private readonly Color32[] _pixelStaging;
    private readonly Dictionary<string, LabelAtlasMetadata> _cache = new( StringComparer.Ordinal );
    private readonly SKPaint _paint;
    private readonly SKPaint _bgPaint;
    private readonly SKFontManager _fontManager;
    private readonly SKTypeface _defaultTypeface;
    private const float BaseFontSize = 14.0f;

    private int _currentX = 10;
    private int _currentY = 10;
    private int _rowHeight = 0;
    private bool _isDirty = false;

    public DynamicUnicodeAtlas( int width = 4096, int height = 4096 )
    {
        AtlasWidth = width;
        AtlasHeight = height;
        _pixelStaging = new Color32[AtlasWidth * AtlasHeight];

        GpuTexture = new Texture2DBuilder()
            .WithName( "g_tDynamicLabelAtlas" )
            .WithSize( AtlasWidth, AtlasHeight )
            .WithFormat( ImageFormat.RGBA8888 )
            .WithDynamicUsage()
            .WithAnonymous( true )
            .Finish();

        Array.Fill( _pixelStaging, new Color32( 0, 0, 0, 0 ) );
        GpuTexture.Update<Color32>( _pixelStaging );

        _fontManager = SKFontManager.Default;
        _defaultTypeface = SKTypeface.FromFamilyName( "Segoe UI" ) ?? SKTypeface.Default;

        _paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White
        };

        _bgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor( 18, 20, 26, 220 ),
            Style = SKPaintStyle.Fill
        };
    }

    public LabelAtlasMetadata GetOrCreateLabel( string text )
    {
        if ( string.IsNullOrEmpty( text ) ) text = "Node";

        if ( _cache.TryGetValue( text, out var meta ) )
            return meta;

        return RasterizeLabel( text );
    }

    private LabelAtlasMetadata RasterizeLabel( string text )
    {
        Vector2 textSize = MeasureUnicodeString( text );
        int itemW = (int)MathF.Ceiling( textSize.x ) + 10;
        int itemH = (int)MathF.Ceiling( textSize.y ) + 6;

        if ( _currentX + itemW > AtlasWidth )
        {
            _currentX = 10;
            _currentY += _rowHeight + 4;
            _rowHeight = 0;
        }

        if ( _currentY + itemH > AtlasHeight )
        {
            // Atlas full fallback
            return new LabelAtlasMetadata
            {
                UvBounds = new Vector4( 0, 0, 0.01f, 0.01f ),
                PixelSize = new Vector2( 20, 10 ),
                IsValid = true
            };
        }

        int allocX = _currentX;
        int allocY = _currentY;

        _currentX += itemW + 4;
        _rowHeight = Math.Max( _rowHeight, itemH );

        using var bitmap = new SKBitmap( itemW, itemH, SKColorType.Bgra8888, SKAlphaType.Premul );
        using var canvas = new SKCanvas( bitmap );
        canvas.Clear( SKColors.Transparent );

        var rect = new SKRoundRect( new SKRect( 0, 0, itemW, itemH ), 4, 4 );
        canvas.DrawRoundRect( rect, _bgPaint );

        DrawUnicodeString( canvas, text, 5.0f, itemH - 5.0f );
        canvas.Flush();

        // Copy sub-rect into pixel staging array
        IntPtr srcPtr = bitmap.GetPixels();
        byte[] bytes = new byte[itemW * itemH * 4];
        Marshal.Copy( srcPtr, bytes, 0, bytes.Length );

        for ( int y = 0; y < itemH; y++ )
        {
            int dstOffset = (allocY + y) * AtlasWidth + allocX;
            int srcOffset = y * itemW * 4;

            for ( int x = 0; x < itemW; x++ )
            {
                int bIdx = srcOffset + (x * 4);
                _pixelStaging[dstOffset + x] = new Color32( bytes[bIdx + 2], bytes[bIdx + 1], bytes[bIdx + 0], bytes[bIdx + 3] );
            }
        }

        var result = new LabelAtlasMetadata
        {
            UvBounds = new Vector4(
                (float)allocX / AtlasWidth,
                (float)allocY / AtlasHeight,
                (float)(allocX + itemW) / AtlasWidth,
                (float)(allocY + itemH) / AtlasHeight
            ),
            PixelSize = new Vector2( itemW, itemH ),
            IsValid = true
        };

        _cache[text] = result;
        _isDirty = true;

        return result;
    }

    public void FlushIfDirty()
    {
        if ( !_isDirty ) return;
        GpuTexture.Update<Color32>( _pixelStaging );
        _isDirty = false;
    }

    private Vector2 MeasureUnicodeString( string text )
    {
        float totalWidth = 0.0f;
        int i = 0;

        while ( i < text.Length )
        {
            int codepoint = char.ConvertToUtf32( text, i );
            int charCount = char.IsSurrogatePair( text, i ) ? 2 : 1;
            string cluster = text.Substring( i, charCount );

            using var typeface = _fontManager.MatchCharacter( codepoint ) ?? _defaultTypeface;
            using var font = new SKFont( typeface, BaseFontSize )
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };

            totalWidth += font.MeasureText( cluster );
            i += charCount;
        }

        return new Vector2( totalWidth, BaseFontSize * 1.2f );
    }

    private void DrawUnicodeString( SKCanvas canvas, string text, float startX, float baselineY )
    {
        float curX = startX;
        int i = 0;

        while ( i < text.Length )
        {
            int codepoint = char.ConvertToUtf32( text, i );
            int charCount = char.IsSurrogatePair( text, i ) ? 2 : 1;
            string cluster = text.Substring( i, charCount );

            using var typeface = _fontManager.MatchCharacter( codepoint ) ?? _defaultTypeface;
            using var font = new SKFont( typeface, BaseFontSize )
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };

            canvas.DrawText( cluster, curX, baselineY, font, _paint );
            curX += font.MeasureText( cluster );

            i += charCount;
        }
    }

    public void Dispose()
    {
        _paint?.Dispose();
        _bgPaint?.Dispose();
        _defaultTypeface?.Dispose();
        _fontManager?.Dispose();
        GpuTexture?.Dispose();
    }
}