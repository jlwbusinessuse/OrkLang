using System.Text;

namespace OrkLang2027.Bytecode
{
    /// <summary>
    /// Reference-type runtime array backing OrkLang's array literals and indexing operators.
    /// Stored on the heap and referenced by <see cref="Value"/> via <see cref="ValueKind.Array"/>,
    /// so assignment/copying a Value around just copies the reference (Java/JS array semantics).
    /// </summary>
    internal sealed class OrkArray
    {
        public List<Value> Items { get; }

        public OrkArray(int capacity = 0) => Items = new List<Value>(capacity);

        public OrkArray(List<Value> items) => Items = items;

        public int Length => Items.Count;

        public Value Get(int index)
        {
            if (index < 0 || index >= Items.Count)
            {
                throw new IndexOutOfRangeException($"Array index {index} out of bounds for length {Items.Count}.");
            }
            return Items[index];
        }

        public void Set(int index, Value value)
        {
            if (index < 0 || index >= Items.Count)
            {
                throw new IndexOutOfRangeException($"Array index {index} out of bounds for length {Items.Count}.");
            }
            Items[index] = value;
        }

        public override string ToString()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Items[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
