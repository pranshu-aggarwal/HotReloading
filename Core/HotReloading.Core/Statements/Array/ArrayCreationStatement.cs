﻿using System.Collections.Generic;

namespace HotReloading.Core.Statements
{
    public class ArrayCreationStatement : Statement
    {
        public Type Type { get; set; }
        public List<Statement> Bounds { get; set; }
        public List<Statement> Initializers { get; set; }
    }
}
