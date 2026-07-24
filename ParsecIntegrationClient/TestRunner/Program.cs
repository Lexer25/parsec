using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using System;
using System.IO;

namespace TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Тестирование SaveState ===\n");

            // Тест 1: Запись с тестовыми данными
            var testFile1 = Path.Combine(Path.GetTempPath(), "test1_state.json");
            var state1 = new State
            {
                IdCardindev = "12345",
                OperationCode = "1",
                Operation = "Добавление_карточки",
                Status = "OK",
                desc = "Тестовое описание операции",
                ErrorMessage = null,
                Attempts = "1",
                Timestamp = new DateTime(2024, 4, 7, 14, 30, 0)
            };

            Console.WriteLine("Тест 1: SaveState с тестовыми данными");
            StateService.SaveState(state1, testFile1);
            
            if (File.Exists(testFile1))
            {
                Console.WriteLine("✓ Файл создан успешно");
                var content = File.ReadAllText(testFile1);
                Console.WriteLine("Содержимое файла:");
                Console.WriteLine(content);
                File.Delete(testFile1);
            }
            else
            {
                Console.WriteLine("✗ Файл не создан!");
            }

            // Тест 2: Запись с ошибкой
            Console.WriteLine("\nТест 2: SaveState с ошибкой");
            var testFile2 = Path.Combine(Path.GetTempPath(), "test2_state.json");
            var state2 = new State
            {
                IdCardindev = "99999",
                OperationCode = "8",
                Operation = "Удаление_группы_доступа_у_карты",
                Status = "ERR",
                desc = "У идентификатора нет привязанной группы доступа",
                ErrorMessage = "ACCGROUP_ID пустой",
                Attempts = "3",
                Timestamp = DateTime.Now
            };

            StateService.SaveState(state2, testFile2);
            
            if (File.Exists(testFile2))
            {
                Console.WriteLine("✓ Файл с ошибкой создан успешно");
                var content = File.ReadAllText(testFile2);
                Console.WriteLine("Содержимое файла:");
                Console.WriteLine(content);
                File.Delete(testFile2);
            }
            else
            {
                Console.WriteLine("✗ Файл не создан!");
            }

            // Тест 3: Создание директории
            Console.WriteLine("\nТест 3: SaveState с созданием директории");
            var randomDir = Path.Combine(Path.GetTempPath(), "test_state_dir_" + Guid.NewGuid().ToString("N"));
            var testFile3 = Path.Combine(randomDir, "state.json");
            var state3 = new State
            {
                IdCardindev = "11111",
                OperationCode = "3",
                Operation = "Добавление_пользователя",
                Status = "OK",
                Timestamp = DateTime.Now
            };

            StateService.SaveState(state3, testFile3);
            
            if (File.Exists(testFile3))
            {
                Console.WriteLine("✓ Файл в новой директории создан успешно");
                Directory.Delete(randomDir, true);
            }
            else
            {
                Console.WriteLine("✗ Файл не создан!");
            }

            Console.WriteLine("\n=== Все тесты завершены ===");
            Console.ReadKey();
        }
    }
}
