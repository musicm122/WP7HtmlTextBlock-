using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Markup;
using System.Xml;
using HtmlAgilityPack;
using System.IO;
using System.Text;

namespace WP7_Mango_HtmlTextBlockControl
{
    public class HtmlTextBlock : Control
    {
        // Constants
        protected const string elementA = "A";
        protected const string elementB = "B";
        protected const string elementBR = "BR";
        protected const string elementEM = "EM";
        protected const string elementI = "I";
        protected const string elementP = "P";
        protected const string elementSTRONG = "STRONG";
        protected const string elementU = "U";

        // Variables
        protected TextBlock _textBlock;
        protected string _text;

        public HtmlTextBlock()
        {
            // Initialize Control by creating a template with a TextBlock
            // TemplateBinding is used to associate Control-based properties
            Template = XamlReader.Load(
                "<ControlTemplate " +
                    "xmlns='http://schemas.microsoft.com/client/2007' " +
                    "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                    "<TextBlock " +
                        "x:Name=\"TextBlock\" " +
                    "/>" +
                "</ControlTemplate>") as ControlTemplate;
            ApplyTemplate();

            // Mirror default TextBlock property values
            var defaultTextBlock = new TextBlock();
            LineHeight = defaultTextBlock.LineHeight;
            LineStackingStrategy = defaultTextBlock.LineStackingStrategy;
            TextAlignment = defaultTextBlock.TextAlignment;
            TextDecorations = defaultTextBlock.TextDecorations;
            TextWrapping = defaultTextBlock.TextWrapping;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // Get a reference to the embedded TextBlock
            _textBlock = GetTemplateChild("TextBlock") as TextBlock;
        }

        protected void ParseAndSetText(string text)
        {
            // Save the original text string
            _text = text;
            // Try for a valid XHTML representation of text
            var success = false;
            try
            {
                // Try to parse as-is
                ParseAndSetSpecifiedText(text);
                success = true;
            }
            catch (XmlException)
            {
                // Invalid XHTML
            }
            if (!success && UseDomAsParser)
            {
                // Fall back on the browser's parsing engine and some custom code
                // Create some DOM nodes to use

                var document = new HtmlDocument();
                // An invisible DIV to contain all the custom content
                var wrapper = document.CreateElement("div");
                //wrapper.SetAttributeValue("style"."display:none");
                //wrapper.SetStyleAttribute("display", "none");
                // A DIV to contain the input to the code
                var input = document.CreateElement("div");
                input.InnerHtml = text;
                //input.SetProperty("innerHTML", text);
                // A DIV to contain the output to the code
                var output = document.CreateElement("div");
                // There should be only one BODY element, but this is an easy way to handle 0 or more
                foreach (var bodyObject in document.GetElementsbyTagName("body"))
                {
                    var body = bodyObject as HtmlNode;
                    if (null != body)
                    {
                        // Add wrapper element to the DOM
                        body.AppendChild(wrapper);
                        try
                        {
                            // Add input/output elements to the DOM
                            wrapper.AppendChild(input);
                            wrapper.AppendChild(output);
                            // Simple code for browsers where .innerHTML returns ~XHTML (ex: Firefox)
                            var transformationSimple =
                                "(function(){" +
                                    "var input = document.body.lastChild.firstChild;" +
                                    "var output = input.nextSibling;" +
                                    "output.innerHTML = input.innerHTML;" +
                                "})();";
                            // Complex code for browsers where .innerHTML returns content as-is (ex: Internet Explorer)
                            var transformationComplex =
                                "(function(){" +
                                    "function computeInnerXhtml(node, inner) {" +
                                        "if (node.nodeValue) {" +
                                            "return node.nodeValue;" +
                                        "} else if (node.nodeName && (0 < node.nodeName.length)) {" +
                                            "var xhtml = '';" +
                                            "if (node.firstChild) {" +
                                                "if (inner) {" +
                                                    "xhtml += '<' + node.nodeName + '>';" +
                                                "}" +
                                                "var child = node.firstChild;" +
                                                "while (child) {" +
                                                    "xhtml += computeInnerXhtml(child, true);" +
                                                    "child = child.nextSibling;" +
                                                "}" +
                                                "if (inner) {" +
                                                    "xhtml += '</' + node.nodeName + '>';" +
                                                "}" +
                                            "} else {" +
                                                "return ('/' == node.nodeName.charAt(0)) ? ('') : ('<' + node.nodeName + '/>');" +
                                            "}" +
                                            "return xhtml;" +
                                        "}" +
                                    "}" +
                                    "var input = document.body.lastChild.firstChild;" +
                                    "var output = input.nextSibling;" +
                                    "output.innerHTML = computeInnerXhtml(input);" +
                                "})();";
                            // Create a list of code options, ordered simple->complex
                            var transformations = new string[] { transformationSimple, transformationComplex };
                            // Try each code option until one works
                            foreach (var transformation in transformations)
                            {
                                // Create a SCRIPT element to contain the code
                                var script = document.CreateElement("script");
                                script.SetAttributeValue("type", "text/javascript");
                                script.InnerHtml= transformation;
                                // Add it to the wrapper element (which runs the code)
                                wrapper.AppendChild(script);
                                // Get the results of the transformation
                                var xhtml = (string)output.InnerHtml ?? "";
                                // Perform some final transformations for the BR element which browsers get wrong
                                xhtml = xhtml.Replace("<br>", "<br/>");  // Firefox
                                xhtml = xhtml.Replace("<BR>", "<BR/>");  // Internet Explorer
                                try
                                {
                                    // Try to parse
                                    ParseAndSetSpecifiedText(xhtml);
                                    success = true;
                                    break;
                                }
                                catch (XmlException)
                                {
                                    // Still invalid XML
                                }
                            }
                        }
                        finally
                        {
                            // Be sure to remove the wrapper we added to the DOM
                            body.RemoveChild(wrapper);
                        }
                        // Processed one BODY; that's enough
                        break;
                    }
                }
            }
            if (!success)
            {
                // Invalid, unfixable XHTML; display the supplied text as-is
                _textBlock.Inlines.Clear();
                _textBlock.Text = _text;
            }
        }

        private void ParseAndSetSpecifiedText(string text)
        {
            // Clear the collection of Inlines
            _textBlock.Inlines.Clear();
            // Wrap the input in a <div> (so even plain text becomes valid XML)
            using (var stringReader = new StringReader(string.Concat("<div>", text, "</div>")))
            {
                // Read the input
                using (var xmlReader = XmlReader.Create(stringReader))
                {
                    // State variables
                    var bold = 0;
                    var italic = 0;
                    var underline = 0;
                    string link = null;
                    var lastElement = elementP;
                    // Read the entire XML DOM...
                    while (xmlReader.Read())
                    {
                        var nameUpper = xmlReader.Name.ToUpper();
                        switch (xmlReader.NodeType)
                        {
                            case XmlNodeType.Element:
                                // Handle the begin element
                                switch (nameUpper)
                                {
                                    case elementA:
                                        link = "";
                                        // Look for the HREF attribute (can't use .MoveToAttribute because it's case-sensitive)
                                        if (xmlReader.MoveToFirstAttribute())
                                        {
                                            do
                                            {
                                                if ("HREF" == xmlReader.Name.ToUpper())
                                                {
                                                    // Store the link target
                                                    link = xmlReader.Value;
                                                    break;
                                                }
                                            } while (xmlReader.MoveToNextAttribute());
                                        }
                                        break;
                                    case elementB:
                                    case elementSTRONG: bold++; break;
                                    case elementI:
                                    case elementEM: italic++; break;
                                    case elementU: underline++; break;
                                    case elementBR: _textBlock.Inlines.Add(new LineBreak()); break;
                                    case elementP:
                                        // Avoid double-space for <p/><p/>
                                        if (lastElement != elementP)
                                        {
                                            _textBlock.Inlines.Add(new LineBreak());
                                            _textBlock.Inlines.Add(new LineBreak());
                                        }
                                        break;
                                }
                                lastElement = nameUpper;
                                break;
                            case XmlNodeType.EndElement:
                                // Handle the end element
                                switch (nameUpper)
                                {
                                    case elementA: link = null; break;
                                    case elementB:
                                    case elementSTRONG: bold--; break;
                                    case elementI:
                                    case elementEM: italic--; break;
                                    case elementU: underline--; break;
                                    case elementBR: _textBlock.Inlines.Add(new LineBreak()); break;
                                    case elementP:
                                        _textBlock.Inlines.Add(new LineBreak());
                                        _textBlock.Inlines.Add(new LineBreak());
                                        break;
                                }
                                lastElement = nameUpper;
                                break;
                            case XmlNodeType.Text:
                            case XmlNodeType.Whitespace:
                                // Create a Run for the visible text
                                // Collapse contiguous whitespace per HTML behavior
                                StringBuilder builder = new StringBuilder(xmlReader.Value.Length);
                                var last = '\0';
                                foreach (char c in xmlReader.Value.Replace('\n', ' '))
                                {
                                    if ((' ' != last) || (' ' != c))
                                    {
                                        builder.Append(c);
                                    }
                                    last = c;
                                }
                                // Trim leading whitespace if following a <P> or <BR> element per HTML behavior
                                var builderString = builder.ToString();
                                if ((elementP == lastElement) || (elementBR == lastElement))
                                {
                                    builderString = builderString.TrimStart();
                                }
                                // If any text remains to display...
                                if (0 < builderString.Length)
                                {
                                    // Create a Run to display it
                                    var run = new Run { Text = builderString };
                                    // Style the Run appropriately
                                    if (0 < bold) run.FontWeight = FontWeights.Bold;
                                    if (0 < italic) run.FontStyle = FontStyles.Italic;
                                    if (0 < underline) run.TextDecorations = System.Windows.TextDecorations.Underline;
                                    if (null != link)
                                    {
                                        // Links get styled and display their HREF since Run doesn't support MouseLeftButton* events
                                        run.TextDecorations = System.Windows.TextDecorations.Underline;
                                        run.Foreground = new SolidColorBrush { Color = Colors.Blue };
                                        run.Text += string.Concat(" <", link, ">");
                                    }
                                    // Add the Run to the collection
                                    _textBlock.Inlines.Add(run);
                                    lastElement = null;
                                }
                                break;
                        }
                    }
                }
            }
        }

        // Custom properties

        // Returns the visible text (i.e., without the HTML tags)
        public string VisibleText
        {
            get { return _textBlock.Text; }
        }

        // Specifies whether the browser DOM can be used to attempt to parse invalid XHTML
        // Note: Deliberately not a DependencyProperty because setting this has security implications
        public bool UseDomAsParser { get; set; }

        // TextBlock properties duplicated so HtmlTextBlock can be used as a TextBlock

        public InlineCollection Inlines
        {
            get { return _textBlock.Inlines; }
        }

        public static DependencyProperty LineHeightProperty = DependencyProperty.Register("LineHeight", typeof(double), typeof(HtmlTextBlock),
            new PropertyMetadata(delegate(DependencyObject o, DependencyPropertyChangedEventArgs e) { ((HtmlTextBlock)o)._textBlock.LineHeight = (double)(e.NewValue); }));
        public double LineHeight
        {
            get { return (double)GetValue(LineHeightProperty); }
            set { SetValue(LineHeightProperty, value); }
        }

        public static DependencyProperty LineStackingStrategyProperty = DependencyProperty.Register("LineStackingStrategy", typeof(LineStackingStrategy), typeof(HtmlTextBlock),
            new PropertyMetadata(delegate(DependencyObject o, DependencyPropertyChangedEventArgs e) { ((HtmlTextBlock)o)._textBlock.LineStackingStrategy = (LineStackingStrategy)(e.NewValue); }));
        public LineStackingStrategy LineStackingStrategy
        {
            get { return (LineStackingStrategy)GetValue(LineStackingStrategyProperty); }
            set { SetValue(LineStackingStrategyProperty, value); }
        }

        public static DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(HtmlTextBlock),
            new PropertyMetadata(delegate(DependencyObject o, DependencyPropertyChangedEventArgs e) { ((HtmlTextBlock)o).ParseAndSetText((string)(e.NewValue)); }));
        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static DependencyProperty TextAlignmentProperty = DependencyProperty.Register("TextAlignment", typeof(TextAlignment), typeof(HtmlTextBlock),
            new PropertyMetadata(delegate(DependencyObject o, DependencyPropertyChangedEventArgs e) { ((HtmlTextBlock)o)._textBlock.TextAlignment = (TextAlignment)(e.NewValue); }));
        public TextAlignment TextAlignment
        {
            get { return (TextAlignment)GetValue(TextAlignmentProperty); }
            set { SetValue(TextAlignmentProperty, value); }
        }

        public static DependencyProperty TextDecorationsProperty = DependencyProperty.Register("TextDecorations", typeof(TextDecorationCollection), typeof(HtmlTextBlock),
            new PropertyMetadata(delegate(DependencyObject o, DependencyPropertyChangedEventArgs e) { ((HtmlTextBlock)o)._textBlock.TextDecorations = (TextDecorationCollection)(e.NewValue); }));
        public TextDecorationCollection TextDecorations
        {
            get { return (TextDecorationCollection)GetValue(TextDecorationsProperty); }
            set { SetValue(TextDecorationsProperty, value); }
        }

        public static DependencyProperty TextWrappingProperty = DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(HtmlTextBlock),
            new PropertyMetadata(delegate(DependencyObject o, DependencyPropertyChangedEventArgs e) { ((HtmlTextBlock)o)._textBlock.TextWrapping = (TextWrapping)(e.NewValue); }));
        public TextWrapping TextWrapping
        {
            get { return (TextWrapping)GetValue(TextWrappingProperty); }
            set { SetValue(TextWrappingProperty, value); }
        }
    }

}
