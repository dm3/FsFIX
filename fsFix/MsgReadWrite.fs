﻿module MsgReadWrite


open System
open Fix44.Fields
open Fix44.FieldWriters
open Fix44.FieldReaders
open Fix44.MessageDU




let CalcCheckSum (bs:byte[]) (pos:int) (len:int) =
    let mutable (sum:byte) = 0uy
    for ctr = pos to (pos + len - 1) do // len is the 'next free index', so it is not included in the checksum calc
        sum <- sum + bs.[ctr]
    //todo: consider a more direct conversion than sprintf
    (sprintf "%03d" sum) |> CheckSum     // checksum is defined as a string in fix44.xml hence the CheckSum field type expects a strings



//let CalcCheckSum (bs:byte[]) (pos:int) (len:int) =
//
//    let tmpBs = Array.zeroCreate<byte> len
//    Array.Copy (bs, pos, tmpBs, 0, len)
//    let ss = System.Text.Encoding.UTF8.GetString tmpBs
//    Diagnostics.Debug.WriteLine ss
//    Diagnostics.Debug.WriteLine ""
//
//    let mutable (sum:byte) = 0uy
//    for ctr = pos to (pos + len - 1) do // len is the 'next free index', so it is not included in the checksum calc
//        sum <- sum + bs.[ctr]
//        let msg = sprintf "%d, " sum
//        Diagnostics.Debug.Write msg
//    
//    Diagnostics.Debug.WriteLine ""
//    //todo: consider a more direct conversion than sprintf
//    // checksum is defined as a string in fix44.xml hence the CheckSum field type expects a string
//    (sprintf "%03d" sum) |> CheckSum 



let WriteTag (dest:byte[]) (nextFreeIdx:int) (msgTag:byte[]) : int = 
    let tag =  [|yield! "35="B; yield! msgTag|]
    System.Buffer.BlockCopy (tag, 0, dest, nextFreeIdx, tag.Length)
    let nextFreeIdx2 = nextFreeIdx + tag.Length
    dest.[nextFreeIdx2] <- 1uy // write the SOH field delimeter
    nextFreeIdx2 + 1 // +1 to go to the index one past the SOH field delimeter


let WriteMessageDU
        (tmpBuf:byte []) 
        (dest:byte []) 
        (nextFreeIdx:int) 
        (beginString:BeginString) 
        (senderCompID:SenderCompID) 
        (targetCompID:TargetCompID) 
        (msgSeqNum:MsgSeqNum) 
        (sendingTime:SendingTime) 
        (msg:FIXMessage) =

    let tag = Fix44.MessageDU.GetTag msg

    let nextFreeIdxInner = WriteTag tmpBuf 0 tag
    let nextFreeIdxInner = WriteMsgSeqNum tmpBuf nextFreeIdxInner msgSeqNum
    let nextFreeIdxInner = WriteSenderCompID tmpBuf nextFreeIdxInner senderCompID
    let nextFreeIdxInner = WriteSendingTime tmpBuf nextFreeIdxInner sendingTime    
    let nextFreeIdxInner = WriteTargetCompID tmpBuf nextFreeIdxInner targetCompID
    
    let innerLen = Fix44.MessageDU.WriteMessage tmpBuf nextFreeIdxInner msg

    let nextFreeIdx = WriteBeginString dest nextFreeIdx beginString
    let nextFreeIdx = WriteBodyLength dest nextFreeIdx (innerLen |> uint32 |> BodyLength)

    System.Buffer.BlockCopy(tmpBuf, 0, dest, nextFreeIdx, innerLen)

    let checksum = CalcCheckSum tmpBuf 0 (innerLen - 1) // -1 so as to not include the final field seperator

    let nextFreeIdx = nextFreeIdx + innerLen

    // no sending optional signature fields in the trailer atm
    //  <trailer>
    //    <field name="SignatureLength" required="N" />
    //    <field name="Signature" required="N" />
    //    <field name="CheckSum" required="Y" />    
    //  </trailer>
    // CheckSum is defined in fix44.xml as a string field, but will always be a three digit number
    
    let nextFreeIdx = WriteCheckSum dest nextFreeIdx checksum 
    nextFreeIdx




// quickfix executor replies to a logon msg by returning a logon msg with the fields in this order
// 8=FIX.4.4
// 9=70
// 35=A
// 34=1
// 49=EXECUTOR
// 52=20161231-07:17:23.037
// 56=CLIENT1
// 98=0
// 108=30
// 10=090

let ReadMessage (bs:byte []) : int * FIXMessage =
    
    let ss = System.Text.Encoding.UTF8.GetString bs
    
    let pos = 0
    let pos, beginString    = ReaderUtils.ReadField bs pos "ReadBeginString" "8"B  ReadBeginString
    let pos, bodyLen        = ReaderUtils.ReadField bs pos "ReadBodyLength" "9"B  ReadBodyLength

    let (BodyLength ulen) = bodyLen
    let len = ulen |> int
    let calcedCheckSum = CalcCheckSum bs pos (len - 1) 

// the generated readMsgType function returns a MsgType DU case which is not used for dispatching
//    let pos, msgType        = ReadMsgType pos src
    let tagValSepPos        = 1 + FIXBuf.findNextTagValSep bs pos
    let pos, tag            = FIXBuf.readValAfterTagValSep bs tagValSepPos

    let pos, seqNum         = ReaderUtils.ReadField bs pos "ReadMsgSeqNum"    "34"B  ReadMsgSeqNum    
    let pos, senderCompID   = ReaderUtils.ReadField bs pos "ReadSenderCompID" "49"B  ReadSenderCompID
    let pos, sendTime       = ReaderUtils.ReadField bs pos "ReadSendingTime"  "52"B  ReadSendingTime
    let pos, targetCompID   = ReaderUtils.ReadField bs pos "ReadTargetCompID" "56"B  ReadTargetCompID

    let pos, msg = ReadMessageDU tag bs pos // reading from the inner buffer, so its pos is not the one to be returned

    let pos, receivedCheckSum   = ReaderUtils.ReadField bs pos "ReadCheckSum" "10"B  ReadCheckSum

    if calcedCheckSum <> receivedCheckSum then
        let msg = sprintf "invalid checksum, received %A, calculated: %A" receivedCheckSum calcedCheckSum
        failwith msg
   
    pos, msg




    
