using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using NUnit.Framework;
using System;
using System.IO;

namespace Tests
{
    [TestFixture]
    public class StateServiceTests
    {
        private string _testFilePath;

        [SetUp]
        public void Setup()
        {
            // Создаем временный путь для тестового файла
            _testFilePath = Path.Combine(Path.GetTempPath(), string.Format("test_state_{0}.json", Guid.NewGuid()));
        }

        [TearDown]
        public void TearDown()
        {
            // Удаляем тестовый файл после теста
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }

        [Test]
        public void SaveState_WithTestData_WritesFileCorrectly()
        {
            // Arrange - создаем тестовые данные
            var state = new State
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

            // Act - вызываем метод SaveState
            StateService.SaveState(state, _testFilePath);

            // Assert - проверяем что файл создан и содержит правильные данные
            Assert.That(File.Exists(_testFilePath), Is.True, "Файл state.json должен быть создан");

            var fileContent = File.ReadAllText(_testFilePath);
            
            // Проверяем что в файле есть ключевые поля
            Assert.That(fileContent, Does.Contain("12345"), "Должен содержать IdCardindev");
            Assert.That(fileContent, Does.Contain("Добавление_карточки"), "Должен содержать Operation");
            Assert.That(fileContent, Does.Contain("OK"), "Должен содержать Status");
            Assert.That(fileContent, Does.Contain("Тестовое описание операции"), "Должен содержать desc");
            Assert.That(fileContent, Does.Contain("2024-04-07"), "Должен содержать Timestamp");
        }

        [Test]
        public void SaveState_WithErrorStatus_WritesErrorDataCorrectly()
        {
            // Arrange - создаем тестовые данные с ошибкой
            var state = new State
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

            // Act
            StateService.SaveState(state, _testFilePath);

            // Assert
            Assert.That(File.Exists(_testFilePath), Is.True, "Файл должен быть создан");

            var fileContent = File.ReadAllText(_testFilePath);
            
            Assert.That(fileContent, Does.Contain("99999"), "Должен содержать IdCardindev");
            Assert.That(fileContent, Does.Contain("ERR"), "Должен содержать статус ERR");
            Assert.That(fileContent, Does.Contain("ACCGROUP_ID пустой"), "Должен содержать ErrorMessage");
        }

        [Test]
        public void SaveState_CreatesDirectoryIfNotExists()
        {
            // Arrange - создаем путь в несуществующей директории
            var randomDir = Path.Combine(Path.GetTempPath(), string.Format("test_dir_{0}", Guid.NewGuid()));
            var filePath = Path.Combine(randomDir, "state.json");
            
            var state = new State
            {
                IdCardindev = "11111",
                OperationCode = "3",
                Operation = "Добавление_пользователя",
                Status = "OK",
                Timestamp = DateTime.Now
            };

            try
            {
                // Act
                StateService.SaveState(state, filePath);

                // Assert
                Assert.That(File.Exists(filePath), Is.True, "Файл должен быть создан в новой директории");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(randomDir))
                {
                    Directory.Delete(randomDir, true);
                }
            }
        }
    }
}
