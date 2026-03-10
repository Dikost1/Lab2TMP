using System;
using System.IO;
using System.Linq;

namespace VirtualMemory
{
    class Program
    {
        private static VirtualArray? _currentArray = null;
        private static string _currentFileName = "";

        static void Main(string[] args)
        {
            Console.WriteLine("Виртуальная память - система управления");
            Console.WriteLine("Введите Help для списка команд");
            Console.WriteLine();

            while (true)
            {
                Console.Write("VM> ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                try
                {
                    ProcessCommand(input.Trim());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }

        static void ProcessCommand(string input)
        {
            string[] parts = ParseCommand(input);
            string command = parts[0].ToLower();

            switch (command)
            {
                case "help":
                    HelpCommand(parts);
                    break;

                case "create":
                    CreateCommand(parts);
                    break;

                case "open":
                    OpenCommand(parts);
                    break;

                case "input":
                    InputCommand(parts);
                    break;

                case "print":
                    PrintCommand(parts);
                    break;

                case "exit":
                    ExitCommand();
                    break;

                default:
                    Console.WriteLine("Неизвестная команда. Введите Help для справки.");
                    break;
            }
        }

        // парсинг команды с учетом кавычек
        static string[] ParseCommand(string input)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            string current = "";

            foreach (char c in input)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    current += c;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        result.Add(current);
                        current = "";
                    }
                }
                else
                {
                    current += c;
                }
            }

            if (!string.IsNullOrEmpty(current))
                result.Add(current);

            return result.ToArray();
        }

        // команда Help
        static void HelpCommand(string[] args)
        {
            if (args.Length == 1)
            {
                Console.WriteLine("Доступные команды:");
                Console.WriteLine("  Create <имя файла>(int|char<длина>|varchar<макс длина>) - создать виртуальный массив");
                Console.WriteLine("  Open <имя файла> - открыть существующий массив");
                Console.WriteLine("  Input <индекс> <значение> - записать значение в элемент");
                Console.WriteLine("  Print <индекс> - вывести значение элемента");
                Console.WriteLine("  Help [команда] - показать справку");
                Console.WriteLine("  Exit - выход");
            }
            else
            {
                string cmd = args[1].ToLower();
                switch (cmd)
                {
                    case "create":
                        Console.WriteLine("Create <имя файла>(int|char<длина>|varchar<макс длина>)");
                        Console.WriteLine("  Создает файл подкачки для виртуального массива");
                        Console.WriteLine("  Пример: Create vm_int.bin(int)");
                        Console.WriteLine("  Пример: Create vm_char.bin(char10)");
                        Console.WriteLine("  Пример: Create vm_varchar.bin(varchar50)");
                        break;
                    case "open":
                        Console.WriteLine("Open <имя файла>");
                        Console.WriteLine("  Открывает существующий файл виртуального массива");
                        break;
                    case "input":
                        Console.WriteLine("Input <индекс> <значение>");
                        Console.WriteLine("  Записывает значение в элемент массива");
                        Console.WriteLine("  Пример: Input 0 42");
                        Console.WriteLine("  Пример: Input 5 \"тестовая строка\"");
                        break;
                    case "print":
                        Console.WriteLine("Print <индекс>");
                        Console.WriteLine("  Выводит значение элемента массива");
                        break;
                    default:
                        Console.WriteLine($"Справка по команде {cmd} недоступна");
                        break;
                }
            }
        }

        // команда Create
        static void CreateCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Укажите имя файла и тип массива");
                Console.WriteLine("Пример: Create vm_int.bin(int)");
                return;
            }

            // разбираем аргумент типа Create name(type) или Create name(typeLength)
            string fileArg = args[1];
            int parenOpen = fileArg.IndexOf('(');
            int parenClose = fileArg.IndexOf(')');

            if (parenOpen < 0 || parenClose < 0)
            {
                Console.WriteLine("Неверный формат. Используйте: Create имя_файла(тип)");
                Console.WriteLine("Типы: int, char<длина>, varchar<макс длина>");
                return;
            }

            string fileName = fileArg.Substring(0, parenOpen);
            string typeStr = fileArg.Substring(parenOpen + 1, parenClose - parenOpen - 1);

            // определяем тип массива
            ArrayType arrayType;
            int stringLength = 0;

            if (typeStr.StartsWith("int"))
            {
                arrayType = ArrayType.Int;
            }
            else if (typeStr.StartsWith("char"))
            {
                arrayType = ArrayType.Char;
                if (typeStr.Length > 4)
                    int.TryParse(typeStr.Substring(4), out stringLength);
                if (stringLength <= 0)
                    stringLength = 10; // значение по умолчанию
            }
            else if (typeStr.StartsWith("varchar"))
            {
                arrayType = ArrayType.Varchar;
                if (typeStr.Length > 7)
                    int.TryParse(typeStr.Substring(7), out stringLength);
                if (stringLength <= 0)
                    stringLength = 50;
            }
            else
            {
                Console.WriteLine("Неизвестный тип. Используйте: int, char<длина>, varchar<макс длина>");
                return;
            }

            // создаем массив
            const long arraySize = 10001; // больше 10000 элементов

            try
            {
                _currentArray?.Close();
                _currentArray = new VirtualArray(fileName, arraySize, arrayType, stringLength);
                _currentArray.Create();
                _currentFileName = fileName;
                Console.WriteLine($"Создан массив: {fileName}");
                Console.WriteLine($"  Размер: {arraySize}");
                Console.WriteLine($"  Тип: {arrayType}");
                if (arrayType != ArrayType.Int)
                    Console.WriteLine($"  Длина строки: {stringLength}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при создании: {ex.Message}");
            }
        }

        // команда Open
        static void OpenCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Укажите имя файла");
                return;
            }

            string fileName = args[1];

            try
            {
                _currentArray?.Close();
                _currentArray = new VirtualArray(fileName, 0, ArrayType.Int);
                _currentArray.Open();
                _currentFileName = fileName;
                Console.WriteLine($"Открыт массив: {fileName}");
                Console.WriteLine($"  Размер: {_currentArray.Size}");
                Console.WriteLine($"  Тип: {_currentArray.Type}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при открытии: {ex.Message}");
            }
        }

        // команда Input
        static void InputCommand(string[] args)
        {
            if (_currentArray == null || !_currentArray.IsOpen)
            {
                Console.WriteLine("Массив не открыт. Используйте Create или Open");
                return;
            }

            if (args.Length < 3)
            {
                Console.WriteLine("Укажите индекс и значение");
                Console.WriteLine("Пример: Input 0 42");
                return;
            }

            if (!int.TryParse(args[1], out int index))
            {
                Console.WriteLine("Неверный индекс");
                return;
            }

            string value = args[2];
            // убираем кавычки если есть
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2);

            try
            {
                if (_currentArray.Type == ArrayType.Int)
                {
                    if (int.TryParse(value, out int intValue))
                    {
                        _currentArray.SetInt(index, intValue);
                        Console.WriteLine($"Записано значение {intValue} в элемент {index}");
                    }
                    else
                    {
                        Console.WriteLine("Ожидалось целое число");
                    }
                }
                else
                {
                    _currentArray.SetString(index, value);
                    Console.WriteLine($"Записана строка \"{value}\" в элемент {index}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка записи: {ex.Message}");
            }
        }

        // команда Print
        static void PrintCommand(string[] args)
        {
            if (_currentArray == null || !_currentArray.IsOpen)
            {
                Console.WriteLine("Массив не открыт. Используйте Create или Open");
                return;
            }

            if (args.Length < 2)
            {
                Console.WriteLine("Укажите индекс");
                return;
            }

            if (!int.TryParse(args[1], out int index))
            {
                Console.WriteLine("Неверный индекс");
                return;
            }

            try
            {
                if (_currentArray.Type == ArrayType.Int)
                {
                    int value = _currentArray.GetInt(index);
                    Console.WriteLine($"Значение элемента [{index}]: {value}");
                }
                else
                {
                    string value = _currentArray.GetString(index);
                    Console.WriteLine($"Значение элемента [{index}]: \"{value}\"");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка чтения: {ex.Message}");
            }
        }

        // команда Exit
        static void ExitCommand()
        {
            Console.WriteLine("Завершение работы...");
            _currentArray?.Close();
            Environment.Exit(0);
        }
    }
}
