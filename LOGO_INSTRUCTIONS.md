# Adding a Custom Logo to AIMonitor+

You can customize AIMonitor+ with your own logo by following these simple steps:

## Logo Requirements

- Format: ICO file (Windows Icon)
- Recommended size: 256x256 pixels
- File name: `logo.ico`

## How to Add Your Logo

1. Create or convert your logo to ICO format
   - You can use online converters like [ConvertICO](https://convertico.com/) or [ICOConvert](https://icoconvert.com/)
   - Or use image editing software like Photoshop or GIMP with ICO export capabilities

2. Place the logo file in the application directory
   - Name the file exactly `logo.ico`
   - Put it in the same folder as the AIMonitor+ executable

3. The application will automatically use your logo when it starts
   - If the logo file is missing or invalid, the default system icon will be used

4. For the installer
   - Place the `logo.ico` file in the same directory as the installer script before building
   - The installer will automatically use your logo for the setup program

## Creating a Multi-Resolution ICO File

For best results, your ICO file should contain multiple resolutions:
- 16x16 pixels (taskbar, file explorer)
- 32x32 pixels (desktop, start menu)
- 48x48 pixels (Windows Explorer large icons)
- 256x256 pixels (high-DPI displays)

Many icon editors and converters can create multi-resolution ICO files from a single source image.

## Troubleshooting

If your logo doesn't appear:
- Make sure it's named exactly `logo.ico` (case sensitive)
- Verify it's a valid ICO file
- Check that it's in the correct directory
- Restart the application after adding the logo file

## Example

```
AIMonitor+ Directory Structure:
├── SimpleMikrotikMonitor.exe
├── logo.ico                   <-- Your custom logo goes here
├── appsettings.json
└── [other application files]
```
