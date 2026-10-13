using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IPdfMergeService
    {
        byte[] MergePdfs(List<byte[]> pdfFiles);
    }

}
