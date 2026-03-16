using System;
using System.IO;
using System.Text;

namespace VirtualMemory
{
    // типы виртуальных массивов
    public enum ArrayType
    {
        Int,      // целые числа
        Char,     // строки фиксированной длины
        Varchar   // строки переменной длины
    }

    // структура страницы, находящейся в памяти
    public class MemoryPage
    {
        public int AbsolutePageNumber;      // номер страницы в файле
        public bool IsModified;              // флаг модификации
        public DateTime LoadTime;            // время загрузки в память
        public byte[] Bitmap = null!;        // битовая карта
        public byte[] Data = null!;          // данные массива на странице
    }

    // класс управления виртуальной памятью
    public class VirtualArray : IDisposable
    {
        private const int PAGE_SIZE = 512;
        private const int MIN_PAGES_IN_MEMORY = 3;
        
        private FileStream? _fileStream;
        private string _fileName;
        private long _arraySize;              // размерность массива
        private ArrayType _arrayType;         // тип массива
        private int _stringLength;            // длина строки для char/varchar
        
        private MemoryPage[] _pageBuffer;     // буфер страниц в памяти
        private int _bufferSize;              // размер буфера
        
        private bool _isOpen;                 // флаг открытого файла
        
        // конструктор
        public VirtualArray(string fileName, long size, ArrayType type, int stringLength = 0)
        {
            _fileName = fileName;
            _arraySize = size;
            _arrayType = type;
            _stringLength = stringLength;
            _bufferSize = MIN_PAGES_IN_MEMORY;
            _pageBuffer = new MemoryPage[_bufferSize];
            _isOpen = false;
        }

        // создание файла подкачки
        public void Create()
        {
            // вычисляем количество страниц
            int pageDataSize = GetPageDataSize();
            long totalBytes = CalculateTotalBytes();
            int pageCount = (int)((totalBytes + PAGE_SIZE - 1) / PAGE_SIZE);

            // создаем файл
            _fileStream = new FileStream(_fileName, FileMode.Create, FileAccess.ReadWrite);
            
            // записываем сигнатуру 'VM'
            _fileStream.Write(new byte[] { (byte)'V', (byte)'M' }, 0, 2);
            
            // записываем блок описания типа массива
            WriteArrayDescriptor(pageCount);
            
            // заполняем файл нулями
            long headerSize = _fileStream.Position;
            long fileSize = headerSize + pageCount * PAGE_SIZE;
            _fileStream.SetLength(fileSize);
            
            // инициализируем страницы в памяти
            InitializePages(pageCount);
            
            _isOpen = true;
        }

        // открытие существующего файла
        public void Open()
        {
            if (!File.Exists(_fileName))
                throw new FileNotFoundException("Файл не найден", _fileName);

            _fileStream = new FileStream(_fileName, FileMode.Open, FileAccess.ReadWrite);
            
            // проверяем сигнатуру
            byte[] signature = new byte[2];
            _fileStream.Read(signature, 0, 2);
            if (signature[0] != (byte)'V' || signature[1] != (byte)'M')
                throw new InvalidDataException("Неверная сигнатура файла");
            
            // читаем дескриптор
            ReadArrayDescriptor();
            
            // загружаем страницы в память
            int pageCount = (int)((_fileStream.Length - _fileStream.Position) / PAGE_SIZE);
            InitializePages(pageCount);
            
            _isOpen = true;
        }

        // вычисление размера данных на странице
        private int GetPageDataSize()
        {
            switch (_arrayType)
            {
                case ArrayType.Int:
                    // 128 элементов * 4 байта = 512 байт (без учета битовой карты)
                    // но с битовой картой: 128 бит = 16 байт, остальное данные
                    return PAGE_SIZE - 16; // 16 байт под битовую карту для 128 элементов
                case ArrayType.Char:
                    return PAGE_SIZE - 16;
                case ArrayType.Varchar:
                    // 128 элементов * 4 байта (указатели) = 512 + 16 байт карта
                    return PAGE_SIZE - 16;
                default:
                    return PAGE_SIZE - 16;
            }
        }

        // вычисление общего размера файла
        private long CalculateTotalBytes()
        {
            switch (_arrayType)
            {
                case ArrayType.Int:
                    return _arraySize * 4; // 4 байта на int
                case ArrayType.Char:
                    return _arraySize * _stringLength;
                case ArrayType.Varchar:
                    return _arraySize * 4; // только указатели в файле подкачки
                default:
                    return _arraySize * 4;
            }
        }

        // запись дескриптора массива
        private void WriteArrayDescriptor(int pageCount)
        {
            // размерность массива (8 байт - long)
            byte[] sizeBytes = BitConverter.GetBytes(_arraySize);
            _fileStream!.Write(sizeBytes, 0, 8);
            
            // тип массива (1 байт)
            _fileStream.WriteByte((byte)_arrayType);
            
            // длина строки (4 байта)
            byte[] strLenBytes = BitConverter.GetBytes(_stringLength);
            _fileStream.Write(strLenBytes, 0, 4);
        }

        // чтение дескриптора массива
        private void ReadArrayDescriptor()
        {
            byte[] sizeBytes = new byte[8];
            _fileStream!.Read(sizeBytes, 0, 8);
            _arraySize = BitConverter.ToInt64(sizeBytes, 0);
            
            _arrayType = (ArrayType)_fileStream.ReadByte();
            
            byte[] strLenBytes = new byte[4];
            _fileStream.Read(strLenBytes, 0, 4);
            _stringLength = BitConverter.ToInt32(strLenBytes, 0);
        }

        // инициализация страниц в памяти
        private void InitializePages(int pageCount)
        {
            // загружаем первые страницы
            int pagesToLoad = Math.Min(_bufferSize, pageCount);
            long headerSize = GetHeaderSize();
            
            for (int i = 0; i < pagesToLoad; i++)
            {
                LoadPage(i, (int)(headerSize + i * PAGE_SIZE));
            }
        }

        // получение размера заголовка
        private long GetHeaderSize()
        {
            return 2 + 8 + 1 + 4; // сигнатура + размер + тип + длина строки
        }

        // загрузка страницы в память
        private void LoadPage(int bufferIndex, long filePosition)
        {
            _fileStream!.Seek(filePosition, SeekOrigin.Begin);
            
            MemoryPage page = new MemoryPage();
            page.Bitmap = new byte[16]; // 128 бит = 16 байт
            page.Data = new byte[PAGE_SIZE - 16];
            
            // читаем битовую карту
            _fileStream.Read(page.Bitmap, 0, 16);
            
            // читаем данные
            _fileStream.Read(page.Data, 0, PAGE_SIZE - 16);
            
            // вычисляем номер страницы
            long headerSize = GetHeaderSize();
            page.AbsolutePageNumber = (int)((filePosition - headerSize) / PAGE_SIZE);
            page.IsModified = false;
            page.LoadTime = DateTime.Now;
            
            _pageBuffer[bufferIndex] = page;
        }

        // сохранение страницы в файл
        private void SavePage(int bufferIndex)
        {
            MemoryPage page = _pageBuffer[bufferIndex];
            if (page == null || !page.IsModified)
                return;
            
            long headerSize = GetHeaderSize();
            long filePosition = headerSize + page.AbsolutePageNumber * PAGE_SIZE;
            
            _fileStream!.Seek(filePosition, SeekOrigin.Begin);
            _fileStream.Write(page.Bitmap, 0, 16);
            _fileStream.Write(page.Data, 0, PAGE_SIZE - 16);
            
            page.IsModified = false;
        }

        // определение номера страницы в буфере по индексу элемента
        private int FindPageInBuffer(int elementIndex)
        {
            int elementsPerPage = GetElementsPerPage();
            int pageNumber = elementIndex / elementsPerPage;
            
            for (int i = 0; i < _bufferSize; i++)
            {
                if (_pageBuffer[i] != null && _pageBuffer[i].AbsolutePageNumber == pageNumber)
                    return i;
            }
            return -1;
        }

        // вычисление количества элементов на странице
        private int GetElementsPerPage()
        {
            switch (_arrayType)
            {
                case ArrayType.Int:
                    return 128; // 512 / 4
                case ArrayType.Char:
                    return 128 * 4 / _stringLength; // примерно
                case ArrayType.Varchar:
                    return 128; // 128 указателей
                default:
                    return 128;
            }
        }

        // получение номера страницы в буфере (с подкачкой если нужно)
        private int GetBufferIndex(int elementIndex)
        {
            // проверка границ
            if (elementIndex < 0 || elementIndex >= _arraySize)
                throw new IndexOutOfRangeException($"Индекс {elementIndex} выходит за границы массива [0, {_arraySize})");
            
            // ищем страницу в буфере
            int bufferIndex = FindPageInBuffer(elementIndex);
            if (bufferIndex >= 0)
                return bufferIndex;
            
            // страница не в памяти - загружаем
            bufferIndex = SelectPageForReplacement();
            int elementsPerPage = GetElementsPerPage();
            int pageNumber = elementIndex / elementsPerPage;
            
            // сохраняем старую страницу если модифицирована
            SavePage(bufferIndex);
            
            // загружаем новую
            long headerSize = GetHeaderSize();
            long filePosition = headerSize + pageNumber * PAGE_SIZE;
            LoadPage(bufferIndex, filePosition);
            
            return bufferIndex;
        }

        // выбор страницы для замещения (самая старая)
        private int SelectPageForReplacement()
        {
            int oldestIndex = 0;
            DateTime oldestTime = _pageBuffer[0].LoadTime;
            
            for (int i = 1; i < _bufferSize; i++)
            {
                if (_pageBuffer[i].LoadTime < oldestTime)
                {
                    oldestTime = _pageBuffer[i].LoadTime;
                    oldestIndex = i;
                }
            }
            return oldestIndex;
        }

        // чтение значения элемента
        public int GetInt(int index)
        {
            int bufferIndex = GetBufferIndex(index);
            int elementsPerPage = GetElementsPerPage();
            int offsetInPage = index % elementsPerPage;
            
            // проверяем битовую карту
            int byteIndex = offsetInPage / 8;
            int bitIndex = offsetInPage % 8;
            if ((_pageBuffer[bufferIndex].Bitmap[byteIndex] & (1 << bitIndex)) == 0)
                throw new InvalidOperationException($"Элемент с индексом {index} не инициализирован");
            
            // читаем данные
            int byteOffset = offsetInPage * 4;
            return BitConverter.ToInt32(_pageBuffer[bufferIndex].Data, byteOffset);
        }

        // запись значения элемента
        public void SetInt(int index, int value)
        {
            int bufferIndex = GetBufferIndex(index);
            int elementsPerPage = GetElementsPerPage();
            int offsetInPage = index % elementsPerPage;
            
            // записываем данные
            int byteOffset = offsetInPage * 4;
            byte[] valueBytes = BitConverter.GetBytes(value);
            Array.Copy(valueBytes, 0, _pageBuffer[bufferIndex].Data, byteOffset, 4);
            
            // устанавливаем бит в карте
            int byteIndex = offsetInPage / 8;
            int bitIndex = offsetInPage % 8;
            _pageBuffer[bufferIndex].Bitmap[byteIndex] |= (byte)(1 << bitIndex);
            
            // помечаем страницу как модифицированную
            _pageBuffer[bufferIndex].IsModified = true;
        }

        // чтение строки (фиксированной длины)
        public string GetString(int index)
        {
            int bufferIndex = GetBufferIndex(index);
            int elementsPerPage = GetElementsPerPage();
            int offsetInPage = index % elementsPerPage;
            
            // проверяем битовую карту
            int byteIndex = offsetInPage / 8;
            int bitIndex = offsetInPage % 8;
            if ((_pageBuffer[bufferIndex].Bitmap[byteIndex] & (1 << bitIndex)) == 0)
                throw new InvalidOperationException($"Элемент с индексом {index} не инициализирован");
            
            // читаем данные
            int byteOffset = offsetInPage * _stringLength;
            byte[] stringData = new byte[_stringLength];
            Array.Copy(_pageBuffer[bufferIndex].Data, byteOffset, stringData, 0, _stringLength);
            
            // удаляем нулевые символы
            return Encoding.ASCII.GetString(stringData).TrimEnd('\0');
        }

        // запись строки (фиксированной длины)
        public void SetString(int index, string value)
        {
            if (value.Length > _stringLength)
                value = value.Substring(0, _stringLength);
            
            int bufferIndex = GetBufferIndex(index);
            int elementsPerPage = GetElementsPerPage();
            int offsetInPage = index % elementsPerPage;
            
            // записываем данные
            int byteOffset = offsetInPage * _stringLength;
            byte[] stringData = Encoding.ASCII.GetBytes(value.PadRight(_stringLength, '\0'));
            Array.Copy(stringData, 0, _pageBuffer[bufferIndex].Data, byteOffset, _stringLength);
            
            // устанавливаем бит в карте
            int byteIndex = offsetInPage / 8;
            int bitIndex = offsetInPage % 8;
            _pageBuffer[bufferIndex].Bitmap[byteIndex] |= (byte)(1 << bitIndex);
            
            _pageBuffer[bufferIndex].IsModified = true;
        }

        // индексатор для удобного доступа
        public int this[int index]
        {
            get { return GetInt(index); }
            set { SetInt(index, value); }
        }

        // закрытие файла
        public void Close()
        {
            if (_fileStream != null)
            {
                // сохраняем все модифицированные страницы
                for (int i = 0; i < _bufferSize; i++)
                {
                    SavePage(i);
                }
                
                _fileStream.Close();
                _fileStream.Dispose();
                _fileStream = null;
            }
            _isOpen = false;
        }

        public void Dispose()
        {
            Close();
        }

        public bool IsOpen => _isOpen;
        public long Size => _arraySize;
        public ArrayType Type => _arrayType;
    }
}
