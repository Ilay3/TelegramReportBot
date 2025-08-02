using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Использование ресурсов
    /// </summary>
    public class ResourceUsage
    {
        /// <summary>
        /// Использование памяти в МБ
        /// </summary>
        public long MemoryUsageMB { get; set; }

        /// <summary>
        /// Использование CPU в процентах
        /// </summary>
        public double CpuUsagePercent { get; set; }

        /// <summary>
        /// Использование диска в МБ
        /// </summary>
        public long DiskUsageMB { get; set; }

        /// <summary>
        /// Свободное место на диске в МБ
        /// </summary>
        public long DiskFreeMB { get; set; }

        /// <summary>
        /// Активные потоки
        /// </summary>
        public int ActiveThreads { get; set; }

        /// <summary>
        /// Открытые дескрипторы
        /// </summary>
        public int OpenHandles { get; set; }

        /// <summary>
        /// Байт отправлено по сети
        /// </summary>
        public long NetworkBytesSent { get; set; }

        /// <summary>
        /// Байт получено по сети
        /// </summary>
        public long NetworkBytesReceived { get; set; }
    }


}
