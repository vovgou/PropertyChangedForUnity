using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fody;
using Mono.Cecil;

public partial class ModuleWeaver
{
    List<TypeDefinition> allClasses;
    Func<TypeDefinition, bool> filter;
    public List<TypeNode> Nodes;
    public List<TypeNode> NotifyNodes;

    private bool IsWeavingAll()
    {
        var attrEle = Config.Attribute(XName.Get("defaultWeaving"));
        if (attrEle == null)
            return true;
        return bool.Parse(attrEle.Value);
    }

    private Func<TypeDefinition, bool> GetFilter()
    {
        if (filter == null)
        {
            if (IsWeavingAll())
                filter = t => !NamespaceFilters.Any() || NamespaceFilters.Any(filter => Regex.IsMatch(t.FullName, filter));
            else
            {
                filter = t => (!NamespaceFilters.Any() || NamespaceFilters.Any(filter => Regex.IsMatch(t.FullName, filter))) && IsEnableWeaving(t);
            }
        }
        return filter;
    }

    public void BuildTypeNodes()
    {
        // setup a filter delegate to apply the namespace filters
         var extraFilter = GetFilter();

        allClasses = ModuleDefinition
            .GetTypes()
            .Where(x => x.IsClass && x.BaseType != null)
            .Where(extraFilter)
            .ToList();
        Nodes = new List<TypeNode>();
        NotifyNodes = new List<TypeNode>();
        TypeDefinition typeDefinition;
        while ((typeDefinition = allClasses.FirstOrDefault()) != null)
        {
            AddClass(typeDefinition);
        }

        PopulateINotifyNodes(Nodes);
        foreach (var notifyNode in NotifyNodes)
        {
            Nodes.Remove(notifyNode);
        }
        PopulateInjectedINotifyNodes(Nodes);
    }

    void PopulateINotifyNodes(List<TypeNode> typeNodes)
    {
        foreach (var node in typeNodes)
        {
            if (HierarchyImplementsINotify(node.TypeDefinition))
            {
                NotifyNodes.Add(node);
                continue;
            }
            PopulateINotifyNodes(node.Nodes);
        }
    }

    void PopulateInjectedINotifyNodes(List<TypeNode> typeNodes)
    {
        foreach (var node in typeNodes)
        {
            if (HasNotifyPropertyChangedAttribute(node.TypeDefinition))
            {
                if (HierarchyImplementsINotify(node.TypeDefinition))
                {
                    throw new WeavingException($"The type '{node.TypeDefinition.FullName}' already implements INotifyPropertyChanged so [AddINotifyPropertyChangedInterfaceAttribute] is redundant.");
                }
                if (node.TypeDefinition.GetPropertyChangedAddMethods().Any())
                {
                    throw new WeavingException($"The type '{node.TypeDefinition.FullName}' already has a PropertyChanged event. If type has a [AddINotifyPropertyChangedInterfaceAttribute] then the PropertyChanged event can be removed.");
                }
                InjectINotifyPropertyChangedInterface(node.TypeDefinition);
                NotifyNodes.Add(node);
                continue;
            }
            PopulateInjectedINotifyNodes(node.Nodes);
        }
    }

    static bool HasNotifyPropertyChangedAttribute(TypeDefinition typeDefinition)
    {
        return typeDefinition.CustomAttributes.ContainsAttribute("PropertyChanged.AddINotifyPropertyChangedInterfaceAttribute");
    }

    TypeNode AddClass(TypeDefinition typeDefinition)
    {
        allClasses.Remove(typeDefinition);
        var typeNode = new TypeNode
        {
            TypeDefinition = typeDefinition
        };
        if (typeDefinition.BaseType.Scope.Name != ModuleDefinition.Name)
        {
            Nodes.Add(typeNode);
        }
        else
        {
            var filter = GetFilter();
            var baseType = Resolve(typeDefinition.BaseType);
            if (filter(baseType))
            {
                var parentNode = FindClassNode(baseType, Nodes);
                if (parentNode == null)
                {
                    parentNode = AddClass(baseType);
                }
                parentNode.Nodes.Add(typeNode);
            }
            else
            {
                Nodes.Add(typeNode);
            }
        }
        return typeNode;
    }

    TypeNode FindClassNode(TypeDefinition type, IEnumerable<TypeNode> typeNode)
    {
        foreach (var node in typeNode)
        {
            if (type == node.TypeDefinition)
            {
                return node;
            }
            var findNode = FindClassNode(type, node.Nodes);
            if (findNode != null)
            {
                return findNode;
            }
        }
        return null;
    }

    private static bool IsEnableWeaving(TypeDefinition typeDefinition)
    {
        return HasNotifyPropertyChangedAttribute(typeDefinition, true) && !(HasDoNotNotifyAttribute(typeDefinition));
    }

    private static bool HasNotifyPropertyChangedAttribute(TypeDefinition typeDefinition, bool inherit = false)
    {
        if (inherit)
            return typeDefinition.GetAllCustomAttributes().ContainsAttribute("PropertyChanged.AddINotifyPropertyChangedInterfaceAttribute");

        return typeDefinition.CustomAttributes.ContainsAttribute("PropertyChanged.AddINotifyPropertyChangedInterfaceAttribute");
    }

    private static bool HasDoNotNotifyAttribute(TypeDefinition typeDefinition)
    {
        return typeDefinition.CustomAttributes.ContainsAttribute("PropertyChanged.DoNotNotifyAttribute");
    }
}