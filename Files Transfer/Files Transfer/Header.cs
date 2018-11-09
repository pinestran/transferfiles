using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Files_Transfer
{
    public enum header : byte
    {
        queue,
        start,
        stop,
        pause,
        chunk
    }
}
