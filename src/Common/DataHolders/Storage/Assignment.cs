﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DispatchSystem.Common.DataHolders.Storage
{
    [Serializable]
    public class Assignment : IDataHolder
    {
        public Guid Id { get; }
        public string Summary { get; }
        public DateTime Creation { get; }

        public Assignment(string summary)
        {
            Summary = summary;
            Creation = DateTime.Now;
            Id = Guid.NewGuid();
        }
        private Assignment() : this(string.Empty) { }

        // For communication
        public static readonly Assignment Empty = new Assignment();
    }
}
