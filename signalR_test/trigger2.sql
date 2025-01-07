CREATE TRIGGER TR_ChargingStorage_Changes 
ON dbo.ChargingStorage 
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO ChargingStorageLog (ChangeType, ChangeDate)
    VALUES ('Change', GETDATE());
END;
