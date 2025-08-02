using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Использование ресурсов
    /// </summary>
    public class ResourceUsage
    {
        public long MemoryUsageMB { get; set; }
        public double CpuUsagePercent { get; set; }
        public long DiskUsageMB { get; set; }
        public long DiskFreeMB { get; set; }

        public int ActiveThreads { get; set; }
        public int OpenHandles { get; set; }

        public long NetworkBytesSent { get; set; }
        public long NetworkBytesReceived { get; set; }
    }

}
