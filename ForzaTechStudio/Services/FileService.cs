using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.Services
{
    public class FileService
    {
        private readonly nint _windowHandle;

        public FileService(nint windowHandle)
        {
            _windowHandle = windowHandle;
        }

        public async Task<IReadOnlyList<string>> PickFilesAsync()
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".modelbin");
            picker.FileTypeFilter.Add(".carbin");
            picker.FileTypeFilter.Add(".zip");

            var files = await picker.PickMultipleFilesAsync();
            return files.Select(f => f.Path).ToList();
        }
    }
}