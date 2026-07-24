using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using System;
using System.IO;

namespace Tests1
{
    [TestClass]
    public class StateServiceTests
    {
        private string _testFilePath;

        [TestInitialize]
        public void Setup()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), string.Format("test_state_{0}.json", Guid.NewGuid()));
        }

        //[TestCleanup]
        //public void TearDown()
        //{
            // Удаляем тестовый файл после теста
          //  if (File.Exists(_testFilePath))
            //{
              //  File.Delete(_testFilePath);
            //}
       // }

        [TestMethod]
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
            Assert.IsTrue(File.Exists(_testFilePath), "Файл state.json должен быть создан");

            var fileContent = File.ReadAllText(_testFilePath);
            
            // Проверяем что в файле есть ключевые поля
            StringAssert.Contains(fileContent, "12345");
            StringAssert.Contains(fileContent, "Добавление_карточки");
            StringAssert.Contains(fileContent, "OK");
            StringAssert.Contains(fileContent, "Тестовое описание операции");
            StringAssert.Contains(fileContent, "2024-04-07");
        }

        [TestMethod]
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
            Assert.IsTrue(File.Exists(_testFilePath), "Файл должен быть создан");

            var fileContent = File.ReadAllText(_testFilePath);
            
            StringAssert.Contains(fileContent, "99999");
            StringAssert.Contains(fileContent, "ERR");
            StringAssert.Contains(fileContent, "ACCGROUP_ID пустой");
        }

        [TestMethod]
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
                Assert.IsTrue(File.Exists(filePath), "Файл должен быть создан в новой директории");
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
