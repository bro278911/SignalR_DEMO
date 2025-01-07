-- 首先確保沒有活動的連接
ALTER DATABASE SignalR SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

-- 啟用 Service Broker
ALTER DATABASE SignalR SET ENABLE_BROKER;

-- 確保資料庫是可信任的
ALTER DATABASE SignalR SET TRUSTWORTHY ON;

-- 重新允許多個連接
ALTER DATABASE SignalR SET MULTI_USER;

-- 確認 Service Broker 是否已啟用
SELECT name, is_broker_enabled, is_trustworthy_on
FROM sys.databases
WHERE name = 'SignalR';

-- 切換到 SignalR 資料庫
USE SignalR;
GO

-- 創建資料庫用戶
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'SignalRUser')
BEGIN
    CREATE USER [SignalRUser] FOR LOGIN [LAPTOP-LI1PKLM3\User]
    ALTER ROLE db_owner ADD MEMBER [SignalRUser]
END
GO

-- 授予權限給資料庫用戶
GRANT VIEW DEFINITION TO [SignalRUser];
GRANT VIEW DATABASE STATE TO [SignalRUser];
GRANT CREATE PROCEDURE TO [SignalRUser];
GRANT CREATE QUEUE TO [SignalRUser];
GRANT CREATE SERVICE TO [SignalRUser];
GRANT REFERENCES ON CONTRACT::[http://schemas.microsoft.com/SQL/Notifications/PostQueryNotification] TO [SignalRUser];

-- 如果測試表不存在，則創建它
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TestMessages]') AND type in (N'U'))
BEGIN
    CREATE TABLE TestMessages (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Message NVARCHAR(MAX),
        CreatedAt DATETIME DEFAULT GETDATE()
    );
END
