using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Configuration;
using Intersect.Core;
using Microsoft.Extensions.Logging;

namespace Intersect.Client.Framework.Gwen.Renderer;


public partial class IntersectRenderer : Base, ICacheToTexture
{

    private bool mClipping = false;

    private FloatRect mClipRect;

    //No Target Needed, Rendering Directly to GUI
    private Color mColor;

    private GameRenderer mRenderer;

    private IGameRenderTexture? mRenderTarget;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnityGwenRenderer" /> class.
    /// </summary>
    /// <param name="target">Intersect render target.</param>
    public IntersectRenderer(IGameRenderTexture renderTarget, GameRenderer renderer)
    {
        mRenderer = renderer;
        mRenderTarget = renderTarget;
    }

    /// <summary>
    ///     Gets or sets the current drawing color.
    /// </summary>
    public override Color DrawColor
    {
        get => Color.FromArgb(mColor.A, mColor.R, mColor.G, mColor.B);
        set => mColor = new Color(value.A, value.R, value.G, value.B);
    }

    public override Color PixelColor(IGameTexture texture, uint x, uint y, Color defaultColor)
    {
        var x1 = (int) x;
        var y1 = (int) y;
        if (texture == null)
        {
            return defaultColor;
        }

        return texture.GetPixel(x1, y1);
    }

    /*
    public override void DrawLine(int x1, int y1, int x2, int y2)
    {
        Translate(ref x1, ref y1);
        Translate(ref x2, ref y2);

        Vertex[] line = {new Vertex(new Vector2(x1, y1), m_Color), new Vertex(new Vector2(x2, y2), m_Color)};

        m_Target.Draw(line, PrimitiveType.Lines);
    }
    */

    /// <summary>
    ///     Returns dimensions of the text using specified font.
    /// </summary>
    /// <param name="font">Font to use.</param>
    /// <param name="fontSize"></param>
    /// <param name="text">Text to measure.</param>
    /// <param name="scale"></param>
    /// <returns>
    ///     Width and height of the rendered text.
    /// </returns>
    public override Point MeasureText(IFont? font, int fontSize, string? text, float scale = 1f)
    {
        if (font == null || string.IsNullOrEmpty(text))
        {
            return default;
        }

        var size = mRenderer.MeasureText(text: text, font: font, size: fontSize, fontScale: scale * Scale);

        return new Point((int) size.X, (int) size.Y);
    }

    public override void RenderText(IFont font, int fontSize, Point pos, string text, float scale = 1f)
    {
        pos = Translate(pos);
        var clip = new FloatRect(ClipRegion.X, ClipRegion.Y, ClipRegion.Width, ClipRegion.Height);
        clip.X = (int) Math.Round(clip.X * Scale);
        clip.Y = (int) Math.Round(clip.Y * Scale);
        clip.Width = (int) Math.Round(clip.Width * Scale);
        clip.Height = (int) Math.Round(clip.Height * Scale);
        mRenderer.DrawString(
            text,
            font,
            size: fontSize,
            pos.X,
            pos.Y,
            Scale * scale,
            mColor,
            false,
            mRenderTarget,
            clip
        );
    }

    public override void DrawFilledRect(Rectangle targetRect)
    {
        var rect = new FloatRect(
            Translate(targetRect).X,
            Translate(targetRect).Y,
            Translate(targetRect).Width,
            Translate(targetRect).Height
        );

        //TODO

        if (mClipping)
        {
            var clip = new FloatRect(ClipRegion.X, ClipRegion.Y, ClipRegion.Width, ClipRegion.Height);
            clip.X = (int) Math.Round(clip.X * Scale);
            clip.Y = (int) Math.Round(clip.Y * Scale);
            clip.Width = (int) Math.Round(clip.Width * Scale);
            clip.Height = (int) Math.Round(clip.Height * Scale);

            float diff = 0;
            if (rect.X < clip.X)
            {
                diff = clip.X - rect.X;
                rect.X += diff;
                rect.Width -= diff;
            }

            if (rect.X + rect.Width > clip.X + clip.Width)
            {
                diff = rect.X + rect.Width - (clip.X + clip.Width);
                rect.Width -= diff;
            }

            if (rect.Y < clip.Y)
            {
                diff = clip.Y - rect.Y;
                rect.Y += diff;
                rect.Height -= diff;
            }

            if (rect.Y + rect.Height > clip.Y + clip.Height)
            {
                diff = rect.Y + rect.Height - (clip.Y + clip.Height);
                rect.Height -= diff;
            }

            if (rect.Width <= 0)
            {
                return;
            }

            if (rect.Height <= 0)
            {
                return;
            }
        }

        if (mRenderTarget == null)
        {
            mRenderer.DrawTexture(
                mRenderer.WhitePixel,
                0,
                0,
                1,
                1,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                mColor,
                mRenderTarget,
                GameBlendModes.None,
                null,
                0f,
                true
            );
        }
        else
        {
            mRenderer.DrawTexture(
                mRenderer.WhitePixel,
                0,
                0,
                1,
                1,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                mColor,
                mRenderTarget,
                GameBlendModes.None,
                null,
                0f,
                true
            );
        }
    }

    public override void DrawTexturedRect(
        IGameTexture? tex,
        Rectangle targetRect,
        Color clr,
        float u1 = 0,
        float v1 = 0,
        float u2 = 1,
        float v2 = 1
    )
    {
        if (null == tex)
        {
            //DrawMissingImage(targetRect);
            return;
        }

        var translatedTarget = Translate(targetRect);
        var rect = new FloatRect(
            translatedTarget.X,
            translatedTarget.Y,
            translatedTarget.Width,
            translatedTarget.Height
        );

        u1 *= tex.Width;
        v1 *= tex.Height;
        u2 *= tex.Width;
        v2 *= tex.Height;

        if (mClipping)
        {
            var clip = new FloatRect(ClipRegion.X, ClipRegion.Y, ClipRegion.Width, ClipRegion.Height);
            clip.X = (int) Math.Round(clip.X * Scale);
            clip.Y = (int) Math.Round(clip.Y * Scale);
            clip.Width = (int) Math.Round(clip.Width * Scale);
            clip.Height = (int) Math.Round(clip.Height * Scale);

            var heightRatio = targetRect.Height * Scale / Math.Abs(v2 - v1);
            var widthRatio = targetRect.Width * Scale / Math.Abs(u2 - u1);

            float diff = 0;
            float vdiff = 0;
            if (rect.X < clip.X)
            {
                diff = clip.X - rect.X;
                vdiff = (int)Math.Floor(diff / widthRatio);
                rect.X += diff;
                rect.Width -= diff;
                u1 += vdiff;
            }

            if (rect.X + rect.Width > clip.X + clip.Width)
            {
                diff = rect.X + rect.Width - (clip.X + clip.Width);
                vdiff = (int)Math.Floor(diff / widthRatio);
                rect.Width -= diff;
                u2 -= vdiff;
            }

            if (rect.Y < clip.Y)
            {
                diff = clip.Y - rect.Y;
                vdiff = (int)Math.Floor(diff / heightRatio);
                rect.Y += diff;
                rect.Height -= diff;
                v1 += vdiff;
            }

            if (rect.Y + rect.Height > clip.Y + clip.Height)
            {
                diff = rect.Y + rect.Height - (clip.Y + clip.Height);
                vdiff = (int)Math.Floor(diff / heightRatio);
                rect.Height -= diff;
                v2 -= vdiff;
            }

            if (rect.Width <= 0)
            {
                return;
            }

            if (rect.Height <= 0)
            {
                return;
            }
        }

        //u1 /= tex.GetWidth();
        //v1 /= tex.GetHeight();
        //u2 /= tex.GetWidth();
        //v2 /= tex.GetHeight();
        if (mRenderTarget == null)
        {
            mRenderer.DrawTexture(
                tex, u1, v1, u2 - u1, v2 - v1, rect.X, rect.Y, rect.Width, rect.Height, mColor, mRenderTarget,
                GameBlendModes.None, null, 0f, true
            );
        }
        else
        {
            mRenderer.DrawTexture(
                tex, u1, v1, u2 - u1, v2 - v1, rect.X, rect.Y, rect.Width, rect.Height, mColor, mRenderTarget,
                GameBlendModes.None, null, 0f, true
            );
        }
    }

    public override void StartClip()
    {
        mClipping = true;
    }

    public override void EndClip()
    {
        mClipping = false;
    }

    private string RemoveExtension(string fileName)
    {
        var fileExtPos = fileName.LastIndexOf(".");
        if (fileExtPos >= 0)
        {
            fileName = fileName.Substring(0, fileExtPos);
        }

        return fileName;
    }

    private string RemoveResourcesSlash(string fileName)
    {
        var resourcesDirectory = ClientConfiguration.ResourcesDirectory;
        if (fileName.ToLower().StartsWith($"{resourcesDirectory}/") || fileName.ToLower().StartsWith($"{resourcesDirectory}\\"))
        {
            fileName = fileName[(resourcesDirectory.Length + 1)..];
        }

        return fileName;
    }

    #region Implementation of ICacheToTexture

    /// <summary>
    /// Cache to texture provider.
    /// </summary>
    public override ICacheToTexture Ctt => this;

    private Dictionary<Control.Base, IGameRenderTexture> m_RT;

    private Stack<IGameRenderTexture> m_Stack;

    private IGameRenderTexture m_RealRT;

    public void Initialize()
    {
        m_RT = new Dictionary<Control.Base, IGameRenderTexture>();
        m_Stack = new Stack<IGameRenderTexture>();
    }

    public void ShutDown()
    {
        m_RT.Clear();
        if (m_Stack.Count > 0)
        {
            throw new InvalidOperationException("Render stack not empty");
        }
    }

    /// <summary>
    /// Called to set the target up for rendering.
    /// </summary>
    /// <param name="control">Control to be rendered.</param>
    // Cache-to-texture debug logging
    private static int _cttLogCount;
    private const int CttLogMax = 200; // Log first N operations then stop
    private static void CttLog(string msg)
    {
        if (_cttLogCount < CttLogMax)
        {
            _cttLogCount++;
            Console.WriteLine($"[CTT] {msg}");
        }
    }

    public void SetupCacheTexture(Control.Base control)
    {
        m_RealRT = mRenderTarget;
        m_Stack.Push(mRenderTarget); // save current RT
        if (!m_RT.TryGetValue(control, out var cacheRt))
        {
            var w = Math.Max(1, control.Width);
            var h = Math.Max(1, control.Height);
            CttLog($"SetupCacheTexture: Creating RT on-demand for '{control.ParentQualifiedName}' {w}x{h}");
            cacheRt = mRenderer.CreateRenderTexture(w, h);
            m_RT[control] = cacheRt;
        }
        mRenderTarget = cacheRt;
        CttLog($"SetupCacheTexture: '{control.ParentQualifiedName}' RT={cacheRt.Width}x{cacheRt.Height} ctrl={control.Width}x{control.Height} stackDepth={m_Stack.Count}");
        mRenderTarget.Begin();
        mRenderTarget.Clear(Color.Transparent);
    }

    /// <summary>
    /// Called when cached rendering is done.
    /// </summary>
    /// <param name="control">Control to be rendered.</param>
    public void FinishCacheTexture(Control.Base control)
    {
        CttLog($"FinishCacheTexture: '{control.ParentQualifiedName}' stackDepth={m_Stack.Count}");
        mRenderTarget.End();
        mRenderTarget = m_Stack.Pop();
        // Restore the world view after framebuffer rendering changed it.
        // End() only restores the viewport, not the projection/view.
        mRenderer.RestoreView();
    }

    /// <summary>
    /// Called when gwen wants to draw the cached version of the control.
    /// </summary>
    /// <param name="control">Control to be rendered.</param>
    public void DrawCachedControlTexture(Control.Base control)
    {
        if (!m_RT.TryGetValue(control, out var ri))
        {
            CttLog($"DrawCachedControlTexture: '{control.ParentQualifiedName}' NO RT — skipping");
            return;
        }

        CttLog($"DrawCachedControlTexture: '{control.ParentQualifiedName}' RT={ri.Width}x{ri.Height} bounds=({control.Bounds.X},{control.Bounds.Y},{control.Bounds.Width},{control.Bounds.Height}) mRenderTarget={(mRenderTarget == null ? "null" : "RT")}");
        var rt = mRenderTarget;
        mRenderTarget = m_RealRT;
        mColor = Color.White;
        DrawTexturedRect(ri, control.Bounds, Color.White);

        mRenderTarget = rt;
    }

    /// <summary>
    /// Called to actually create a cached texture.
    /// </summary>
    /// <param name="control">Control to be rendered.</param>
    public void CreateControlCacheTexture(Control.Base control)
    {
        if (m_RT.TryGetValue(control, out var existing))
        {
            // Recreate if the control has been resized since the cache was created
            if (existing.Width != control.Width || existing.Height != control.Height)
            {
                CttLog($"CreateControlCacheTexture: RESIZE '{control.ParentQualifiedName}' old={existing.Width}x{existing.Height} new={control.Width}x{control.Height}");
                existing.Dispose();
                m_RT.Remove(control);
            }
            else
            {
                return;
            }
        }

        // Create new cache RT matching current control dimensions
        if (control.Width > 0 && control.Height > 0)
        {
            CttLog($"CreateControlCacheTexture: CREATE '{control.ParentQualifiedName}' {control.Width}x{control.Height}");
            m_RT[control] = mRenderer.CreateRenderTexture(control.Width, control.Height);
            m_RT[control].Clear(Color.Transparent);
        }
        else
        {
            CttLog($"CreateControlCacheTexture: SKIP '{control.ParentQualifiedName}' — zero size {control.Width}x{control.Height}");
        }
    }

    public void DisposeCachedTexture(Control.Base control)
    {
        if (m_RT.ContainsKey(control))
        {
            var rt = m_RT[control];
            rt.Dispose();
            m_RT.Remove(control);
        }
    }

    public void UpdateControlCacheTexture(Control.Base control)
    {
        throw new NotImplementedException();
    }

    public void SetRenderer(Base renderer)
    {
        throw new NotImplementedException();
    }

    #endregion

}
