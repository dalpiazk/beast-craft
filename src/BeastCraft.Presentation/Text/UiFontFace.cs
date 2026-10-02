namespace BeastCraft.Presentation.Text
{
    /// <summary>
    /// Which of the UI's two typefaces a piece of text draws in. <see cref="Heading"/> is Fredoka
    /// SemiBold — headings, buttons, tabs, chips, captions: every face the toolkit drew in before the
    /// body typeface landed, and still the default (<c>ITextRenderer</c>'s original methods, with no
    /// face parameter, always mean <see cref="Heading"/>, so no existing call site changes meaning).
    /// <see cref="Body"/> is Atkinson Hyperlegible — long-form reading text (a paragraph, a
    /// description, the credits), chosen for its accessibility (distinct letterforms at small sizes,
    /// designed with low-vision readers in mind). A caller opts into <see cref="Body"/> through
    /// <c>ITextRenderer</c>'s face-aware overloads (<c>UiPainter.Label</c>'s <c>Label.Face</c>).
    /// </summary>
    public enum UiFontFace
    {
        Heading,
        Body
    }
}
