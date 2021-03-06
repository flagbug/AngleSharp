﻿namespace AngleSharp.Dom.Svg
{
    using AngleSharp.Extensions;
    using AngleSharp.Html;
    using System;

    /// <summary>
    /// Represents an element of the SVG DOM.
    /// </summary>
    internal class SvgElement : Element, ISvgElement
    {
        #region ctor

        static SvgElement()
        {
            RegisterCallback<SvgElement>(AttributeNames.Style, (element, value) => element.UpdateStyle(value));
        }

        public SvgElement(Document owner, String name, String prefix = null, NodeFlags flags = NodeFlags.None)
            : base(owner, name, prefix, NamespaceNames.SvgUri, flags | NodeFlags.SvgMember)
        {
        }

        #endregion

        #region Methods

        public override INode Clone(Boolean deep = true)
        {
            var node = Factory.SvgElements.Create(Owner, LocalName, Prefix);
            CloneElement(node, deep);
            return node;
        }

        #endregion

        #region Internal Methods

        internal override void SetupElement()
        {
            base.SetupElement();

            var style = this.GetOwnAttribute(AttributeNames.Style);

            if (style != null)
            {
                UpdateStyle(style);
            }
        }

        #endregion
    }
}
