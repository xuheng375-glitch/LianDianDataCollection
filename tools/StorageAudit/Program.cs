using System;
using System.Diagnostics;
using System.IO;
using LianDian.Core.Config;
using LianDian.Data;

internal static class Program
{
    private static int Main(string[] args)
    {
        // 仅创建临时 SQLite 数据库，不加载通信或业务层，不操作生产数据库。
        string directory=Path.Combine(Path.GetTempPath(),"LianDianStorageAudit",Guid.NewGuid().ToString("N"));
        var config=new DbConfig {FilePath=Path.Combine(directory,"audit.db"),BackupDirectory=Path.Combine(directory,"backups"),MinimumFreeSpaceMb=256};
        try
        {
            int total = args.Length == 0 ? 3654000 : int.Parse(args[0]);
            if (total < 10000 || total > 10000000) throw new ArgumentOutOfRangeException(nameof(total));
            new DatabaseInitializer(config).EnsureCreated();
            var context=new DataContext(config);var repo=new BatchRepository(context);
            Console.WriteLine("SQLite engine: "+context.ExecuteScalar("SELECT sqlite_version()"));
            var watch=Stopwatch.StartNew();
            context.ExecuteNonQuery(@"WITH RECURSIVE rows(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM rows WHERE n<@total)
                INSERT INTO batch_record(batch_date,batch_no,product_name,employee_no,issue_time,qr_grade,voltage,resistance,current,withstand_result,pressure,pressure_result)
                SELECT printf('%02d%03d',21+(n-1)/730000,1+((n-1)/2000)%365),1+(n-1)%2000,
                'PRODUCT-'||(n%20),'600249','2026-09-08 00:00:00','A',220.5,10.2,0.8,1,0.5,1 FROM rows", DataContext.Param("@total",total));
            Console.WriteLine("Seed "+total+" rows (one SQL transaction): "+watch.ElapsedMilliseconds+" ms");
            watch.Restart();var page=repo.QueryPage("21001","26365",null,0,500);
            Console.WriteLine("First page (500 rows): "+watch.ElapsedMilliseconds+" ms");
            if(page.Count!=500)throw new Exception("Page count mismatch");
            watch.Restart();var filtered=repo.QueryPage("21001","26365","PRODUCT-1",0,500);
            Console.WriteLine("Filtered page (500 rows): "+watch.ElapsedMilliseconds+" ms");
            if(filtered.Count!=500)throw new Exception("Filtered page count mismatch");
            watch.Restart();int count=0;string previousDate=null;int previousNo=0;
            foreach(var row in repo.ExportRows("21001","26365",null))
            {
                if(previousDate!=null && (string.CompareOrdinal(row.BatchDate,previousDate)>0 ||
                    (row.BatchDate==previousDate && row.BatchNo>=previousNo))) throw new Exception("Order/duplicate mismatch");
                previousDate=row.BatchDate;previousNo=row.BatchNo;count++;
                if(count % 500000 == 0) Console.WriteLine("Traversed "+count+" rows / "+watch.ElapsedMilliseconds+" ms");
            }
            Console.WriteLine("Stream export traversal: "+count+" rows / "+watch.ElapsedMilliseconds+" ms");
            if(count!=total)throw new Exception("Export count mismatch");
            context.ExecuteNonQuery("PRAGMA wal_checkpoint(PASSIVE)");
            Console.WriteLine("Database bytes: "+new FileInfo(config.FilePath).Length);
            watch.Restart();
            using(var backup=new DatabaseBackupService(context,config))
            {
                string saved=backup.BackupNow();
                Console.WriteLine("Backup + quick_check: "+watch.ElapsedMilliseconds+" ms");
                var restored=new DataContext(new DbConfig {FilePath=saved});
                if(Convert.ToInt64(restored.ExecuteScalar("SELECT COUNT(*) FROM batch_record"))!=total)throw new Exception("Backup restore mismatch");
            }
            Console.WriteLine("Peak working set bytes: "+Process.GetCurrentProcess().PeakWorkingSet64);
            Console.WriteLine("PASS; temporary database: "+config.FilePath);
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
