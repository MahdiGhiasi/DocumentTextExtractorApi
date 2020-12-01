// From https://www.codeproject.com/Articles/20529/Using-DocxToText-to-Extract-Text-from-DOCX-Files

using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Text;
using System.Xml;

namespace DocumentTextExtractorApi.Classes
{
    public class DocxToText
    {
        private const string ContentTypeNamespace =
            @"http://schemas.openxmlformats.org/package/2006/content-types";

        private const string WordprocessingMlNamespace =
            @"http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private const string DocumentXmlXPath =
            "/t:Types/t:Override[@ContentType=\"" +
            "application/vnd.openxmlformats-officedocument." +
            "wordprocessingml.document.main+xml\"]";

        private const string BodyXPath = "/w:document/w:body";

        private string docxFile = "";
        private Stream docxStream = null;
        private string docxFileLocation = "";

        public DocxToText(string fileName)
        {
            docxFile = fileName;

        }

        public DocxToText(Stream fileStream)
        {
            docxFile = null;
            docxStream = fileStream;
        }

        #region ExtractText()
        /// 
        /// Extracts text from the Docx file.
        /// 
        /// Extracted text.
        public string ExtractText()
        {
            if (string.IsNullOrEmpty(docxFile) && docxStream == null)
                throw new Exception("Input file not specified.");

            // Usually it is "/word/document.xml"

            docxFileLocation = FindDocumentXmlLocation();

            if (string.IsNullOrEmpty(docxFileLocation))
                throw new Exception("It is not a valid Docx file.");

            return ReadDocumentXml();
        }
        #endregion

        #region FindDocumentXmlLocation()
        /// 
        /// Gets location of the "document.xml" zip entry.
        /// 
        /// Location of the "document.xml".
        private string FindDocumentXmlLocation()
        {
            ZipFile zip = OpenDocxFileAsZipFile();
            foreach (ZipEntry entry in zip)
            {
                // Find "[Content_Types].xml" zip entry

                if (string.Compare(entry.Name, "[Content_Types].xml", true) == 0)
                {
                    Stream contentTypes = zip.GetInputStream(entry);

                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.PreserveWhitespace = true;
                    xmlDoc.Load(contentTypes);
                    contentTypes.Close();

                    //Create an XmlNamespaceManager for resolving namespaces

                    XmlNamespaceManager nsmgr =
                        new XmlNamespaceManager(xmlDoc.NameTable);
                    nsmgr.AddNamespace("t", ContentTypeNamespace);

                    // Find location of "document.xml"

                    XmlNode node = xmlDoc.DocumentElement.SelectSingleNode(
                        DocumentXmlXPath, nsmgr);

                    if (node != null)
                    {
                        string location =
                            ((XmlElement)node).GetAttribute("PartName");
                        return location.TrimStart(new char[] { '/' });
                    }
                    break;
                }
            }
            zip.Close();
            return null;
        }

        private ZipFile OpenDocxFileAsZipFile()
        {
            ZipFile zip;
            if (docxStream == null)
                zip = new ZipFile(docxFile);
            else
                zip = new ZipFile(docxStream, true);
            return zip;
        }
        #endregion

        #region ReadDocumentXml()
        /// 
        /// Reads "document.xml" zip entry.
        /// 
        /// Text containing in the document.
        private string ReadDocumentXml()
        {
            StringBuilder sb = new StringBuilder();

            ZipFile zip = OpenDocxFileAsZipFile();
            foreach (ZipEntry entry in zip)
            {
                if (string.Compare(entry.Name, docxFileLocation, true) == 0)
                {
                    Stream documentXml = zip.GetInputStream(entry);

                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.PreserveWhitespace = true;
                    xmlDoc.Load(documentXml);
                    documentXml.Close();

                    XmlNamespaceManager nsmgr =
                        new XmlNamespaceManager(xmlDoc.NameTable);
                    nsmgr.AddNamespace("w", WordprocessingMlNamespace);

                    XmlNode node =
                        xmlDoc.DocumentElement.SelectSingleNode(BodyXPath, nsmgr);

                    if (node == null)
                        return string.Empty;

                    sb.Append(ReadNode(node));

                    break;
                }
            }
            zip.Close();
            return sb.ToString();
        }
        #endregion

        #region ReadNode()
        /// 
        /// Reads content of the node and its nested childs.
        /// 
        /// XmlNode.
        /// Text containing in the node.
        private string ReadNode(XmlNode node)
        {
            if (node == null || node.NodeType != XmlNodeType.Element)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                switch (child.LocalName)
                {
                    case "t":                           // Text
                        sb.Append(child.InnerText.TrimEnd());

                        string space =
                            ((XmlElement)child).GetAttribute("xml:space");
                        if (!string.IsNullOrEmpty(space) &&
                            space == "preserve")
                            sb.Append(' ');

                        break;

                    case "cr":                          // Carriage return
                    case "br":                          // Page break
                        sb.Append(Environment.NewLine);
                        break;

                    case "tab":                         // Tab
                        sb.Append("\t");
                        break;

                    case "p":                           // Paragraph
                        sb.Append(ReadNode(child));
                        sb.Append(Environment.NewLine);
                        sb.Append(Environment.NewLine);
                        break;

                    default:
                        sb.Append(ReadNode(child));
                        break;
                }
            }
            return sb.ToString();
        }
        #endregion
    }
}