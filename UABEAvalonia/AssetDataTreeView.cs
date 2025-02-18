﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    public class AssetDataTreeView : TreeView
    {
        private AssetWorkspace workspace;

        private AvaloniaList<TreeViewItem> ListItems => (AvaloniaList<TreeViewItem>)Items;

        public void Init(AssetWorkspace workspace)
        {
            this.workspace = workspace;
            Reset();
        }

        public void Reset()
        {
            Items = new AvaloniaList<TreeViewItem>();
        }

        public void LoadComponent(AssetContainer container)
        {
            if (workspace == null)
                return;

            AssetTypeValueField baseField = workspace.GetBaseField(container);

            string baseItemString = $"{baseField.GetFieldType()} {baseField.GetName()}";
            if (container.ClassId == (uint)AssetClassID.MonoBehaviour)
            {
                string monoName = Extensions.GetMonoBehaviourNameFast(workspace, container);
                if (monoName != null)
                {
                    baseItemString += $" ({monoName})";
                }
            }

            TreeViewItem baseItem = CreateTreeItem(baseItemString);

            TreeViewItem arrayIndexTreeItem = CreateTreeItem("Loading...");
            baseItem.Items = new AvaloniaList<TreeViewItem>() { arrayIndexTreeItem };
            ListItems.Add(baseItem);

            SetTreeItemEvents(baseItem, container.FileInstance, container.PathId, baseField);
            baseItem.IsExpanded = true;
        }

        public void ExpandAllChildren(TreeViewItem treeItem)
        {
            if (treeItem.Header is string header)
            {
                if (header != "[view asset]")
                {
                    treeItem.IsExpanded = true;

                    foreach (TreeViewItem treeItemChild in treeItem.Items)
                    {
                        ExpandAllChildren(treeItemChild);
                    }
                }
            }
        }

        public void CollapseAllChildren(TreeViewItem treeItem)
        {
            if (treeItem.Header is string header)
            {
                if (header != "[view asset]")
                {
                    foreach (TreeViewItem treeItemChild in treeItem.Items)
                    {
                        CollapseAllChildren(treeItemChild);
                    }

                    treeItem.IsExpanded = false;
                }
            }
        }

        private TreeViewItem CreateTreeItem(string text)
        {
            return new TreeViewItem() { Header = text };
        }

        //lazy load tree items. avalonia is really slow to load if
        //we just throw everything in the treeview at once
        private void SetTreeItemEvents(TreeViewItem item, AssetsFileInstance fromFile, long fromPathId, AssetTypeValueField field)
        {
            item.Tag = new AssetDataTreeViewItem(fromFile, fromPathId);
            //avalonia's treeviews have no Expanded event so this is all we can do
            var expandObs = item.GetObservable(TreeViewItem.IsExpandedProperty);
            expandObs.Subscribe(isExpanded =>
            {
                AssetDataTreeViewItem itemInfo = (AssetDataTreeViewItem)item.Tag;
                if (isExpanded && !itemInfo.loaded)
                {
                    itemInfo.loaded = true; //don't load this again
                    TreeLoad(fromFile, field, fromPathId, item);
                }
            });
        }

        private void SetPPtrEvents(TreeViewItem item, AssetsFileInstance fromFile, long fromPathId, AssetContainer cont)
        {
            item.Tag = new AssetDataTreeViewItem(fromFile, fromPathId);
            var expandObs = item.GetObservable(TreeViewItem.IsExpandedProperty);
            expandObs.Subscribe(isExpanded =>
            {
                AssetDataTreeViewItem itemInfo = (AssetDataTreeViewItem)item.Tag;
                if (isExpanded && !itemInfo.loaded)
                {
                    itemInfo.loaded = true; //don't load this again

                    if (cont != null)
                    {
                        AssetTypeValueField baseField = workspace.GetBaseField(cont);
                        TreeViewItem baseItem = CreateTreeItem($"{baseField.GetFieldType()} {baseField.GetName()}");

                        TreeViewItem arrayIndexTreeItem = CreateTreeItem("Loading...");
                        baseItem.Items = new AvaloniaList<TreeViewItem>() { arrayIndexTreeItem };
                        item.Items = new AvaloniaList<TreeViewItem>() { baseItem };
                        SetTreeItemEvents(baseItem, cont.FileInstance, fromPathId, baseField);
                    }
                    else
                    {
                        item.Items = new AvaloniaList<TreeViewItem>() { CreateTreeItem("[null asset]") };
                    }
                }
            });
        }

        private void TreeLoad(AssetsFileInstance fromFile, AssetTypeValueField assetField, long fromPathId, TreeViewItem treeItem)
        {
            if (assetField.childrenCount == 0) return;

            int arrayIdx = 0;
            AvaloniaList<TreeViewItem> items = new AvaloniaList<TreeViewItem>(assetField.childrenCount + 1);

            AssetTypeTemplateField assetFieldTemplate = assetField.GetTemplateField();
            bool isArray = assetFieldTemplate.isArray;

            if (isArray)
            {
                int size = assetField.GetValue().AsArray().size;
                AssetTypeTemplateField sizeTemplate = assetFieldTemplate.children[0];
                TreeViewItem arrayIndexTreeItem = CreateTreeItem($"{sizeTemplate.type} {sizeTemplate.name} = {size}");
                items.Add(arrayIndexTreeItem);
            }

            foreach (AssetTypeValueField childField in assetField.children)
            {
                if (childField == null) return;
                string value = "";
                if (childField.GetValue() != null)
                {
                    EnumValueTypes evt = childField.GetValue().GetValueType();
                    string quote = "";
                    if (evt == EnumValueTypes.String) quote = "\"";
                    if (1 <= (int)evt && (int)evt <= 12)
                    {
                        value = $" = {quote}{childField.GetValue().AsString()}{quote}";
                    }
                    if (evt == EnumValueTypes.Array)
                    {
                        value = $" (size {childField.childrenCount})";
                    }
                    else if (evt == EnumValueTypes.ByteArray)
                    {
                        value = $" (size {childField.GetValue().AsByteArray().size})";
                    }
                }

                if (isArray)
                {
                    TreeViewItem arrayIndexTreeItem = CreateTreeItem($"{arrayIdx}");
                    items.Add(arrayIndexTreeItem);

                    TreeViewItem childTreeItem = CreateTreeItem($"{childField.GetFieldType()} {childField.GetName()}{value}");
                    arrayIndexTreeItem.Items = new AvaloniaList<TreeViewItem>() { childTreeItem };

                    if (childField.childrenCount > 0)
                    {
                        TreeViewItem dummyItem = CreateTreeItem("Loading...");
                        childTreeItem.Items = new AvaloniaList<TreeViewItem>() { dummyItem };
                        SetTreeItemEvents(childTreeItem, fromFile, fromPathId, childField);
                    }

                    arrayIdx++;
                }
                else
                {
                    TreeViewItem childTreeItem = CreateTreeItem($"{childField.GetFieldType()} {childField.GetName()}{value}");
                    items.Add(childTreeItem);

                    if (childField.childrenCount > 0)
                    {
                        TreeViewItem dummyItem = CreateTreeItem("Loading...");
                        childTreeItem.Items = new AvaloniaList<TreeViewItem>() { dummyItem };
                        SetTreeItemEvents(childTreeItem, fromFile, fromPathId, childField);
                    }
                }
            }

            string templateFieldType = assetField.templateField.type;
            if (templateFieldType.StartsWith("PPtr<") && templateFieldType.EndsWith(">"))
            {
                var fileIdField = assetField.Get("m_FileID");
                var pathIdField = assetField.Get("m_PathID");
                bool pptrValid = !fileIdField.IsDummy() && !pathIdField.IsDummy();

                if (pptrValid)
                {
                    int fileId = fileIdField.GetValue().AsInt();
                    long pathId = pathIdField.GetValue().AsInt64();

                    AssetContainer cont = workspace.GetAssetContainer(fromFile, fileId, pathId, true);

                    TreeViewItem childTreeItem = CreateTreeItem("[view asset]");
                    items.Add(childTreeItem);

                    TreeViewItem dummyItem = CreateTreeItem("Loading...");
                    childTreeItem.Items = new AvaloniaList<TreeViewItem>() { dummyItem };
                    SetPPtrEvents(childTreeItem, fromFile, pathId, cont);
                }
            }

            treeItem.Items = items;
        }
    }

    public class AssetDataTreeViewItem
    {
        public bool loaded;
        public AssetsFileInstance fromFile;
        public long fromPathId;

        public AssetDataTreeViewItem(AssetsFileInstance fromFile, long fromPathId)
        {
            this.loaded = false;
            this.fromFile = fromFile;
            this.fromPathId = fromPathId;
        }
    }
}
