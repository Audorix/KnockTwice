# Assets

| File | Used for |
| --- | --- |
| `social-preview.png` | GitHub social preview (Settings > General > Social preview), 1280 × 640 |
| `social-preview.html` | Source of the social preview image |

To regenerate the image after editing the HTML (Windows, Microsoft Edge):

```powershell
& "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe" --headless=new --hide-scrollbars --force-device-scale-factor=1 --window-size=1280,640 --screenshot="$PWD\docs\assets\social-preview.png" "file:///$PWD/docs/assets/social-preview.html"
```

Then upload it again in the repository settings.
