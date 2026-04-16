using System.Collections.Generic;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MeshPaint
{
    /// <summary>
    /// Owns one <see cref="MeshPaintSessionEntry"/> per (Renderer, material slot) and
    /// tracks which one is currently foregrounded in the Mesh Painter v2 window.
    ///
    /// Switching the active entry swaps which preview texture is installed on its
    /// material so that each Renderer keeps its own live preview while the user moves
    /// between meshes — without a save/discard dialog.
    /// </summary>
    internal class MeshPaintSessionManager
    {
        private readonly Dictionary<Key, MeshPaintSessionEntry> _entries = new Dictionary<Key, MeshPaintSessionEntry>();
        public MeshPaintSessionEntry Active { get; private set; }

        public IEnumerable<MeshPaintSessionEntry> AllEntries => _entries.Values;
        public int EntryCount => _entries.Count;

        public MeshPaintSessionEntry GetOrCreate(Renderer r, GameObject avatarRoot, int slot)
        {
            if (r == null) return null;
            var key = new Key(r, slot);
            if (_entries.TryGetValue(key, out var existing))
                return existing;

            var entry = new MeshPaintSessionEntry();
            if (!entry.Begin(r, avatarRoot, slot))
                return null;

            _entries[key] = entry;
            return entry;
        }

        /// <summary>
        /// Foreground <paramref name="entry"/>. Suspends the previously-active
        /// entry's preview (restores original texture on its material) and
        /// resumes the new one (installs its preview texture).
        /// </summary>
        public void SetActive(MeshPaintSessionEntry entry)
        {
            if (Active == entry) return;
            if (Active != null)
                Active.Session.Suspend();
            Active = entry;
            if (Active != null)
                Active.Session.Resume();
        }

        public MeshPaintSessionEntry FindEntry(Renderer r, int slot)
        {
            if (r == null) return null;
            _entries.TryGetValue(new Key(r, slot), out var e);
            return e;
        }

        public bool HasAnyOps()
        {
            foreach (var e in _entries.Values)
                if (e.Ops.Count > 0) return true;
            return false;
        }

        public void DisposeAll()
        {
            foreach (var e in _entries.Values)
                e.Dispose();
            _entries.Clear();
            Active = null;
        }

        /// <summary>
        /// Remove an entry (e.g., when the Renderer is destroyed). Disposes its session.
        /// </summary>
        public void DropEntry(MeshPaintSessionEntry entry)
        {
            if (entry == null) return;
            var toRemove = new List<Key>();
            foreach (var kv in _entries)
                if (kv.Value == entry) toRemove.Add(kv.Key);
            foreach (var k in toRemove)
                _entries.Remove(k);
            entry.Dispose();
            if (Active == entry) Active = null;
        }

        private readonly struct Key
        {
            public readonly Renderer Renderer;
            public readonly int Slot;
            public Key(Renderer r, int slot) { Renderer = r; Slot = slot; }

            public override bool Equals(object obj)
                => obj is Key k && ReferenceEquals(k.Renderer, Renderer) && k.Slot == Slot;

            public override int GetHashCode()
            {
                int h = Renderer != null ? Renderer.GetInstanceID() : 0;
                return (h * 397) ^ Slot;
            }
        }
    }
}
