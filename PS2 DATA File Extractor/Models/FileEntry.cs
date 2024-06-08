using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS2_DATA_File_Extractor.Models
{
    public class FileEntry
    {
        public long HeaderStart { get; set; }
        public long HeaderEnd { get; set; }
        public int StringLength { get; set; }
        public string Path { get; set; }
        public int Offset { get; set; }
        public int OriginalSize { get; set; }
        public int CurrentSize { get; set; }
    }
}
