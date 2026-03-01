SET NOCOUNT ON;

---------------------------------------------------------
-- 1. Infrastructure Setup: Credential & Schema
---------------------------------------------------------

-- Create the Database Scoped Credential if it does not exist
-- Note: Identity 'X-Api-Key' maps the header name for the API call
IF NOT EXISTS (SELECT * FROM sys.database_scoped_credentials WHERE name = 'ApiTokenCredential')
BEGIN
    CREATE DATABASE SCOPED CREDENTIAL [ApiTokenCredential]
    WITH IDENTITY = 'X-Api-Key', SECRET = 'INSECURE-CHANGE-ME-api-key';
END

-- Ensure the LatestToken column exists in Bpa.Users
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Bpa.Users') AND name = 'LatestToken')
BEGIN
    ALTER TABLE Bpa.Users ADD LatestToken NVARCHAR(MAX);
END

---------------------------------------------------------
-- 2. Execution Logic
---------------------------------------------------------

-- Configuration Variables
DECLARE @url NVARCHAR(4000) = 'http://localhost:5000/api/tokens/generate';
DECLARE @headers NVARCHAR(MAX) = N'{"Content-Type": "application/json"}';

-- Iteration Variables
DECLARE @Email NVARCHAR(255);
DECLARE @payload NVARCHAR(MAX);
DECLARE @response NVARCHAR(MAX);
DECLARE @ret INT;
DECLARE @ExtractedToken NVARCHAR(MAX);

-- Cursor to process records from Bpa.Users
DECLARE UserCursor CURSOR FOR 
SELECT Email 
FROM Bpa.Users;

OPEN UserCursor;
FETCH NEXT FROM UserCursor INTO @Email;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Construct JSON payload as an array (endpoint always expects an array; single item = array of one)
    -- CAST is used for BIT types to ensure JSON boolean (true/false) output
    SET @payload = (
        SELECT
            'q1-campaign' AS [bucket],
            @Email AS [email],
            JSON_QUERY('{"newsletter": true, "marketing": false}') AS [permissions],
            CAST(0 AS BIT) AS [skipPermissionUpdate],
            CAST(1 AS BIT) AS [allowReplay],
            60 AS [expiryDays]
        FOR JSON PATH
    );

    BEGIN TRY
        -- Execute the REST call utilizing the Database Scoped Credential
        EXEC @ret = sp_invoke_external_rest_endpoint
            @method = 'POST',
            @url = @url,
            @headers = @headers,
            @payload = @payload,
            @credential = [ApiTokenCredential],
            @response = @response OUTPUT;

        -- Check Procedure Return Code (0 = Success) and HTTP Status Code
        IF @ret = 0 AND JSON_VALUE(@response, '$.response.status') IN ('200', '201')
        BEGIN
            -- Parse the token from the nested JSON result (response is an array; read first element)
            SET @ExtractedToken = JSON_VALUE(@response, '$.result[0].token');

            -- Update the record
            UPDATE Bpa.Users
            SET LatestToken = @ExtractedToken
            WHERE Email = @Email;
        END
        ELSE
        BEGIN
            -- Output failure metadata for debugging
            RAISERROR('API Request Failed for %s. Response: %s', 10, 1, @Email, @response);
        END
    END TRY
    BEGIN CATCH
        -- Catch network errors or procedure execution errors
        PRINT 'Error processing email ' + @Email + ': ' + ERROR_MESSAGE();
    END CATCH

    FETCH NEXT FROM UserCursor INTO @Email;
END

CLOSE UserCursor;
DEALLOCATE UserCursor;