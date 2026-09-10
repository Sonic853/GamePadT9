# 便携包构建缓存

`mixed-cache.zip` 供 GitHub Actions 在未安装小白 T9 的 Windows 环境中打包。该文件使用 Git LFS，检出仓库时需要下载 LFS 实体文件。

归档包含从小白 T9 2026.08.04 的已部署 `xiaobai_simp` 数据生成、经运行验证的混合拼音索引（Rime 1.13.1）。仅包含编译后的 `.prism.bin`、`.schema.yaml`、内容指纹清单和 `ready` 标记，不包含原始词库、Rime 引擎、用户学习数据或本机配置。当前内容指纹为 `9e5e388f82c076b94f7a297a8dcbf9aec734d7d29529cea28c264a41f90735ae`。

`scripts/import-mixed-cache.ps1` 在复制前核对清单、完成标记和两份编译文件的 SHA256，仅复制这些明确列出的文件。目标电脑的小白方案或词库与此缓存不匹配时，主程序仍会按现有逻辑在本机重新生成索引。

更新缓存时，在已安装目标版本小白 T9 的开发电脑上启动一次 GamePad T9，完成缓存生成并退出；然后在项目根目录执行：

```powershell
$cacheExport = Join-Path (Get-Location) ('artifacts/cache-export-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project src/GamePadT9/GamePadT9.csproj -c Release -- --export-mixed-cache $cacheExport
if ($LASTEXITCODE -ne 0) { throw '缓存导出失败。' }
$cacheZip = $cacheExport + '.zip'
Push-Location -LiteralPath $cacheExport
try {
    7z a -tzip -mm=Deflate -mx=9 -mfb=258 -mpass=15 -mmt=off -mtc=off -mta=off $cacheZip '*'
    if ($LASTEXITCODE -ne 0) { throw '缓存压缩失败。' }
    7z t $cacheZip
    if ($LASTEXITCODE -ne 0) { throw '缓存归档校验失败。' }
} finally { Pop-Location }
Copy-Item -LiteralPath $cacheZip -Destination data/mixed-cache.zip -Force
./tests/Packaging/CacheArchive.Tests.ps1
```

使用全新的导出目录和 ZIP，避免将旧版索引或额外文件混入。更新此说明中的来源版本及内容指纹，并将归档和文档一并提交；普通界面或按键逻辑修改无需更新缓存。
