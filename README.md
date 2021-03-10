# GZipTest
Console application for compressing/decompressing files

## Task description
Разработать консольное приложение на C# для поблочного сжатия и распаковки файлов с помощью System.IO.Compression.GzipStream.
Для сжатия исходный файл делится на блоки одинакового размера, например, в 1 мегабайт. Каждый блок сжимается и записывается в выходной файл независимо от остальных блоков.
Программа должна эффективно распараллеливать и синхронизировать обработку блоков в многопроцессорной среде и уметь обрабатывать файлы, размер которых превышает объем доступной оперативной памяти. 
В случае исключительных ситуаций необходимо проинформировать пользователя понятным сообщением, позволяющим пользователю исправить возникшую проблему, в частности, если проблемы связаны с ограничениями операционной системы.
При работе с потоками допускается использовать только базовые классы и объекты синхронизации (Thread, Manual/AutoResetEvent, Monitor, Semaphor, Mutex) и не допускается использовать async/await, ThreadPool, BackgroundWorker, TPL.
Код программы должен соответствовать принципам ООП и ООД (читаемость, разбиение на классы и т.д.). 
Параметры программы, имена исходного и результирующего файлов должны задаваться в командной строке следующим образом:
GZipTest.exe compress/decompress [имя исходного файла] [имя результирующего файла]
В случае успеха программа должна возвращать 0, при ошибке возвращать 1.
Примечание: формат архива остаётся на усмотрение автора, и не имеет значения для оценки качества тестового, в частности соответствие формату GZIP опционально.

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
