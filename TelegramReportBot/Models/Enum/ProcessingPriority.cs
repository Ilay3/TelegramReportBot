using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models.Enum
{
    /// <summary>
    /// Приоритет обработки файла
    /// </summary>
    public enum ProcessingPriority
    {
        Low = 1,
        Normal = 2,
        High = 3,
        Critical = 4
    }

}
