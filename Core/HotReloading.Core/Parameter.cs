using HotReloading.Core.Statements;

namespace HotReloading.Core
{
    public class Parameter : Statement
    {
        public string Name { get; set; }
        public Type Type { get; set; }
    }
}