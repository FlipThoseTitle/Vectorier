#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger;

public class Template
{
    public XmlNode mainTemplateNode;

    public Template(XmlNode node)
    {
        mainTemplateNode = node;
    }
}
