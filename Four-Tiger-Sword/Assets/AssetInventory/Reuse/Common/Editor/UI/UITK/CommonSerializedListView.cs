using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// A serialized-array authoring surface backed by Unity's native ListView.
    /// Use this for flat, fixed-height inspector collections that need Undo and prefab-safe edits.
    /// </summary>
    public sealed class CommonSerializedListView : VisualElement
    {
        const float DefaultItemHeight = 24f;

        readonly SerializedObject _serializedObject;
        readonly string _propertyPath;
        readonly List<int> _indices = new List<int>();
        readonly Button _addButton;
        readonly Button _removeButton;
        readonly HelpBox _multiObjectHelp;
        bool _isRefreshing;

        public CommonSerializedListView(
            SerializedObject serializedObject,
            string propertyPath,
            string viewDataKey = null)
        {
            _serializedObject = serializedObject ?? throw new ArgumentNullException(nameof(serializedObject));
            _propertyPath = !string.IsNullOrWhiteSpace(propertyPath)
                ? propertyPath
                : throw new ArgumentException("A serialized array property path is required.", nameof(propertyPath));

            SerializedProperty property = GetArrayProperty();
            if (property == null || !property.isArray || property.propertyType == SerializedPropertyType.String)
                throw new ArgumentException("The property path must resolve to a serialized array or List.", nameof(propertyPath));

            AddToClassList("common-serialized-list");

            ItemList = new ListView(_indices, DefaultItemHeight, MakeItem, BindItem)
            {
                name = "common-serialized-list-view",
                selectionType = SelectionType.Single,
                reorderable = CanModifyStructure,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                viewDataKey = viewDataKey
            };
            ItemList.AddToClassList("common-serialized-list__items");
            ItemList.itemIndexChanged += OnItemIndexChanged;
            ItemList.selectionChanged += _ => UpdateRemoveButton();
            Add(ItemList);

            VisualElement footer = CommonUITK.CreateContainer("common-serialized-list__footer");
            _removeButton = CommonUITK.CreateButton("−", RemoveSelectedItem, "common-serialized-list__button");
            _removeButton.tooltip = "Remove the selected item";
            _addButton = CommonUITK.CreateButton("+", () => AddItem(), "common-serialized-list__button");
            _addButton.tooltip = "Add an item";
            footer.Add(_removeButton);
            footer.Add(_addButton);
            Add(footer);

            _multiObjectHelp = CommonUITK.CreateHelpBox(
                "List structure cannot be changed while editing multiple objects. Individual values remain editable.",
                HelpBoxMessageType.Info,
                "common-serialized-list__multi-object-help");
            _multiObjectHelp.style.display = CanModifyStructure ? DisplayStyle.None : DisplayStyle.Flex;
            Add(_multiObjectHelp);

            Refresh(true);
        }

        public ListView ItemList { get; }

        public bool CanModifyStructure => !_serializedObject.isEditingMultipleObjects;

        public bool AddItem()
        {
            if (!CanModifyStructure)
                return false;

            return Mutate("Add List Item", property => property.arraySize += 1);
        }

        public bool RemoveItemAt(int index)
        {
            if (!CanModifyStructure)
                return false;

            SerializedProperty property = GetArrayProperty();
            if (property == null || index < 0 || index >= property.arraySize)
                return false;

            return Mutate("Remove List Item", array =>
            {
                int previousSize = array.arraySize;
                array.DeleteArrayElementAtIndex(index);
                if (array.arraySize == previousSize)
                    array.DeleteArrayElementAtIndex(index);
            });
        }

        public bool MoveItem(int sourceIndex, int destinationIndex)
        {
            if (!CanModifyStructure)
                return false;

            SerializedProperty property = GetArrayProperty();
            if (property == null ||
                sourceIndex < 0 ||
                destinationIndex < 0 ||
                sourceIndex >= property.arraySize ||
                destinationIndex >= property.arraySize ||
                sourceIndex == destinationIndex)
            {
                return false;
            }

            return Mutate(
                "Reorder List Item",
                array => array.MoveArrayElement(sourceIndex, destinationIndex));
        }

        public void RefreshItems()
        {
            Refresh(false);
        }

        VisualElement MakeItem()
        {
            VisualElement row = CommonUITK.CreateContainer("common-serialized-list__row");
            return row;
        }

        void BindItem(VisualElement row, int index)
        {
            row.Unbind();
            row.Clear();

            SerializedProperty array = GetArrayProperty();
            if (array == null || index < 0 || index >= array.arraySize)
                return;

            SerializedProperty element = array.GetArrayElementAtIndex(index).Copy();
            PropertyField field = new PropertyField(element)
            {
                name = "common-serialized-list-item-" + index
            };
            field.AddToClassList("common-serialized-list__field");
            row.Add(field);

            // ListView rows may be created after the inspector's automatic binding pass.
            field.Bind(_serializedObject);
        }

        void OnItemIndexChanged(int sourceIndex, int destinationIndex)
        {
            if (_isRefreshing)
                return;

            MoveItem(sourceIndex, destinationIndex);
        }

        void RemoveSelectedItem()
        {
            RemoveItemAt(ItemList.selectedIndex);
        }

        bool Mutate(string undoName, Action<SerializedProperty> mutation)
        {
            _serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property = GetArrayProperty();
            if (property == null)
                return false;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObjects(_serializedObject.targetObjects, undoName);
            mutation(property);
            bool changed = _serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);

            if (!changed)
                return false;

            Refresh(true);
            return true;
        }

        SerializedProperty GetArrayProperty()
        {
            return _serializedObject.FindProperty(_propertyPath);
        }

        void Refresh(bool structureChanged)
        {
            _serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property = GetArrayProperty();
            int itemCount = property != null ? property.arraySize : 0;

            _isRefreshing = true;
            _indices.Clear();
            for (int i = 0; i < itemCount; i++)
                _indices.Add(i);

            if (structureChanged)
                ItemList.Rebuild();
            else
                ItemList.RefreshItems();
            _isRefreshing = false;

            ItemList.reorderable = CanModifyStructure;
            _addButton.SetEnabled(CanModifyStructure);
            _multiObjectHelp.style.display = CanModifyStructure ? DisplayStyle.None : DisplayStyle.Flex;
            UpdateRemoveButton();
        }

        void UpdateRemoveButton()
        {
            _removeButton.SetEnabled(
                CanModifyStructure &&
                ItemList.selectedIndex >= 0 &&
                ItemList.selectedIndex < _indices.Count);
        }
    }
}
