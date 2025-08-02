using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models.Enum
{
    /// <summary>
    /// Типы отчётов для раздельной рассылки
    /// </summary>
    public enum ReportType
    {
        All,        // Все файлы
        UserErrors, // Только пользовательские ошибки  
        ServerErrors, // Только серверные ошибки
        Warnings    // Только предупреждения
    }
}
