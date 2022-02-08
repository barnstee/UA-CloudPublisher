
using System;

namespace UANodesetWebViewer.Models
{
    public class JSTreeEntry : IComparable
    {
        public string id;

        public string text;

        public string nodeClass;

        public string accessLevel;

        public string eventNotifier;

        public string executable;

        public bool children;

        public bool publishedNode;

        public JSTreeEntry()
        {
            id = string.Empty;
            text = string.Empty;
            nodeClass = string.Empty;
            accessLevel = string.Empty;
            eventNotifier = string.Empty;
            executable = string.Empty;
            children = false;
            publishedNode = false;
        }

        public override bool Equals(object obj)
        {
            JSTreeEntry other = obj as JSTreeEntry;
            if (other != null)
            {
                return (id == other.id) && (text == other.text) && (nodeClass == other.nodeClass);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return id.GetHashCode() ^ text.GetHashCode() ^ nodeClass.GetHashCode();
        }

        int IComparable.CompareTo(object obj)
        {
            JSTreeEntry other = obj as JSTreeEntry;
            if (other != null)
            {
                return string.Compare(text, other.text);
            }
            else
            {
                return -1;
            }
        }
    }
}
