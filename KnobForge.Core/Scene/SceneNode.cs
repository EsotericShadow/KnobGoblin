using System;
using System.Collections.Generic;

namespace KnobForge.Core.Scene
{
    public class SceneNode
    {
        private readonly List<SceneNode> _children = new();

        public SceneNode(string name = "Node")
        {
            Name = name;
            Id = Guid.NewGuid();
        }

        public Guid Id { get; }
        public string Name { get; set; }
        public SceneNode? Parent { get; private set; }
        public IReadOnlyList<SceneNode> Children => _children;

        public void AddChild(SceneNode child)
        {
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            if (ReferenceEquals(child, this))
            {
                throw new InvalidOperationException("A node cannot be its own child.");
            }

            if (child.Parent != null)
            {
                child.Parent.RemoveChild(child);
            }

            child.Parent = this;
            _children.Add(child);
        }

        public bool RemoveChild(SceneNode child)
        {
            if (child == null)
            {
                return false;
            }

            bool removed = _children.Remove(child);
            if (removed && ReferenceEquals(child.Parent, this))
            {
                child.Parent = null;
            }

            return removed;
        }
    }
}
