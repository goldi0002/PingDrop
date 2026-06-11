using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Models
{
    internal sealed record class PDMetrics(string Label, string Value, string Icon = "", string Sub = "")
    {
        public static PDMetrics Empty => new(string.Empty, string.Empty);   
    }

}
