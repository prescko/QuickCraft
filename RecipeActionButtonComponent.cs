using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace QuickCraft;

internal sealed class RecipeActionButtonComponent : RichTextComponentBase
{
    private const double UnscaledHeight = 24.0;
    private const double HorizontalPadding = 8.0;

    private sealed class FixedBounds : ElementBounds
    {
        public override double bgDrawX => absFixedX;
        public override double bgDrawY => absFixedY;
        public override double renderX => absFixedX + renderOffsetX;
        public override double renderY => absFixedY + renderOffsetY;
        public override double absX => absFixedX;
        public override double absY => absFixedY;

        public FixedBounds(double width, double height)
        {
            absInnerWidth = fixedWidth = width;
            absInnerHeight = fixedHeight = height;
            BothSizing = ElementSizing.Fixed;
            ParentBounds = new ElementBounds();
        }
    }

    private readonly Action action;
    private readonly string tooltip;
    private readonly FixedBounds bounds;
    private readonly GuiElementTextButton button;
    private readonly GuiElementHoverText hover;
    private double hoverSeconds;
    private double lastRenderX;
    private double lastRenderY;

    public RecipeActionButtonComponent(ICoreClientAPI api, string label, string langCode, double[] color, Action action)
        : base(api)
    {
        this.action = action;
        tooltip = Lang.Get(ModIds.ModId + ":" + langCode);
        Float = EnumFloat.Inline;
        VerticalAlign = EnumVerticalAlign.Middle;

        double height = Math.Ceiling(GuiElement.scaled(UnscaledHeight));
        double width = Math.Ceiling(GuiElement.scaled(label.Length * 8.0 + HorizontalPadding * 2.0));
        bounds = new FixedBounds(width, height);

        CairoFont normalFont = CairoFont.WhiteMediumText().WithColor(color);
        CairoFont pressedFont = CairoFont.WhiteMediumText().WithColor(color);
        ((FontConfig)normalFont).UnscaledFontsize = GuiElement.scaled(16.0);
        ((FontConfig)pressedFont).UnscaledFontsize = GuiElement.scaled(16.0);

        button = new GuiElementTextButton(api, label, normalFont, pressedFont, NoopClick, bounds, EnumButtonStyle.Small)
        {
            PlaySound = false
        };
        button.ComposeElements(null, null);

        hover = new GuiElementHoverText(api, tooltip, CairoFont.WhiteSmallText(), 220, bounds, null);
        hover.SetAutoDisplay(false);
    }

    public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offsetX, double lineY, out double nextOffsetX)
    {
        double width = bounds.fixedWidth;
        double height = bounds.fixedHeight;
        BoundsPerLine = new[]
        {
            new LineRectangled(offsetX + GuiElement.scaled(5.0), lineY + GuiElement.scaled(2.0), width, height)
        };
        nextOffsetX = offsetX + width + GuiElement.scaled(10.0);
        return EnumCalcBoundsResult.Continue;
    }

    public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
    {
        if (!HandbookSpaceCrafting.CanUseCraftingActions(api))
        {
            hoverSeconds = 0;
            return;
        }

        lastRenderX = renderX;
        lastRenderY = renderY;
        SetBounds(lastRenderX, lastRenderY);
        button.RenderInteractiveElements(deltaTime);

        if (bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
        {
            hoverSeconds += deltaTime;
        }
        else
        {
            hoverSeconds = 0;
        }

        hover.SetVisible(hoverSeconds > 0.75);
        hover.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (HandbookSpaceCrafting.CanUseCraftingActions(api))
        {
            SetBounds(lastRenderX, lastRenderY);

            if (args.Button == EnumMouseButton.Left && bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
            {
                args.Handled = true;
                button.OnMouseDown(api, args);
                action();
                return;
            }

            button.OnMouseDown(api, args);
        }
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (HandbookSpaceCrafting.CanUseCraftingActions(api))
        {
            SetBounds(lastRenderX, lastRenderY);
            button.OnMouseUp(api, args);
        }
    }

    public override void OnMouseMove(MouseEvent args)
    {
        if (HandbookSpaceCrafting.CanUseCraftingActions(api))
        {
            SetBounds(lastRenderX, lastRenderY);
            button.PlaySound = true;
            button.OnMouseMove(api, args);
            button.PlaySound = false;
        }
    }

    public override void Dispose()
    {
        button.Dispose();
        hover.Dispose();
    }

    private static bool NoopClick() => true;

    private void SetBounds(double xOffset = 0.0, double yOffset = 0.0)
    {
        if (BoundsPerLine == null || BoundsPerLine.Length == 0)
        {
            return;
        }

        LineRectangled rect = BoundsPerLine[0];
        bounds.absInnerWidth = rect.Width;
        bounds.absInnerHeight = rect.Height;
        bounds.absFixedX = xOffset + rect.X;
        bounds.absFixedY = yOffset + rect.Y;
    }
}
