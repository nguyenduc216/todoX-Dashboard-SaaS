# TodoX Publish

Chạy `01_Publish-To-Local-Publish.bat`

Script sẽ:
1. dotnet clean
2. Xóa toàn bộ nội dung `TodoX.Web\publish`
3. `dotnet publish -c Release -o TodoX.Web\publish`

Không tác động IIS.
