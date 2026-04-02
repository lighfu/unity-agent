using UnityEngine;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor.MA
{
    /// <summary>
    /// Fluent builder for MA menu hierarchies.
    /// Creates holder GO + MenuInstaller + MenuItem(SubMenu) + child items.
    /// </summary>
    internal class MAMenuBuilder
    {
        public GameObject Root { get; private set; }

        private MAMenuBuilder(GameObject root)
        {
            Root = root;
        }

        /// <summary>
        /// Create a new menu hierarchy under the avatar root.
        /// Creates a holder GameObject with MenuInstaller + MenuItem(SubMenu).
        /// </summary>
        public static MAMenuBuilder Create(GameObject avatarRoot, string holderName,
            Texture2D icon = null)
        {
            if (!MAAvailability.IsInstalled)
                return null;

            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create MA Menu Holder");
            holder.transform.SetParent(avatarRoot.transform, false);

            MAComponentFactory.AddMenuInstaller(holder);
            MAComponentFactory.AddMenuItemSubMenu(holder, icon);

            return new MAMenuBuilder(holder);
        }

        /// <summary>
        /// Add a toggle child item under the menu hierarchy.
        /// </summary>
        public MAMenuBuilder AddToggle(string displayName, string paramName,
            bool isDefault = false, bool synced = true, bool saved = true,
            float value = 1f, Texture2D icon = null)
        {
            var child = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(child, "Create MA Toggle Item");
            child.transform.SetParent(Root.transform, false);

            MAComponentFactory.AddMenuItemToggle(child, paramName, value,
                synced, saved, isDefault, icon);

            return this;
        }

        /// <summary>
        /// Add a radial puppet child item under the menu hierarchy.
        /// </summary>
        public MAMenuBuilder AddRadial(string displayName, string paramName,
            Texture2D icon = null)
        {
            var child = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(child, "Create MA Radial Item");
            child.transform.SetParent(Root.transform, false);

            MAComponentFactory.AddMenuItemRadial(child, paramName, icon);

            return this;
        }

        /// <summary>
        /// Add a button child item under the menu hierarchy.
        /// </summary>
        public MAMenuBuilder AddButton(string displayName, string paramName = null,
            float value = 1f, Texture2D icon = null)
        {
            var child = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(child, "Create MA Button Item");
            child.transform.SetParent(Root.transform, false);

            MAComponentFactory.AddMenuItemButton(child, paramName, value, icon);

            return this;
        }

        /// <summary>
        /// Add a sub-menu child item and return a nested builder for it.
        /// </summary>
        public MAMenuBuilder AddSubMenu(string displayName, Texture2D icon = null)
        {
            var child = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(child, "Create MA SubMenu Item");
            child.transform.SetParent(Root.transform, false);

            MAComponentFactory.AddMenuItemSubMenu(child, icon);

            return new MAMenuBuilder(child);
        }
    }
}
