# GZipTest
Console application for compressing/decompressing files

## Task description
Application for multithread compressing and decompressing files using GZipStream. The file is splited into blocks of fixes size. Each block is compressed independently in separate thread using GZipStream and written to the file. If success it returns 0, otherwise 1.

## GZip test console tool description

### Compression

```console
.\GZipTest.Console.exe compress --path <sourceFilePath> --output <outputFilePath>
```

 -p, --path      
 Required. Path to the file to be processed

 -o, --output    
 Required. Path to the output file
 
 
### Decompression

```console
.\GZipTest.Console.exe decompress --path <sourceFilePath> --output <outputFilePath>
```

 -p, --path      
 Required. Path to the file to be processed

 -o, --output    
 Required. Path to the output file


## GZipTest usage

Example for compressing:
```console
.\GZipTest.Console.exe compress --path text.txt --output text.txt.gz
```

Example for decompressing:
```console
.\GZipTest.Console.exe decompress --path text.txt.gz --output text.txt
```
