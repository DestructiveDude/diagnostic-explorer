
namespace WidgetSample
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            gadgetGrid = new System.Windows.Forms.DataGridView();
            bAddGadget = new System.Windows.Forms.Button();
            label1 = new System.Windows.Forms.Label();
            bAddWidget = new System.Windows.Forms.Button();
            widgetGrid = new System.Windows.Forms.DataGridView();
            label2 = new System.Windows.Forms.Label();
            bInfo = new System.Windows.Forms.Button();
            bHorrificException = new System.Windows.Forms.Button();
            bRemoveGadget = new System.Windows.Forms.Button();
            bRemoveWidget = new System.Windows.Forms.Button();
            btnTraceScope = new System.Windows.Forms.Button();
            btn100 = new System.Windows.Forms.Button();
            btn1000 = new System.Windows.Forms.Button();
            btn10 = new System.Windows.Forms.Button();
            chkSystem = new System.Windows.Forms.CheckBox();
            chkWidgets = new System.Windows.Forms.CheckBox();
            chkGadgets = new System.Windows.Forms.CheckBox();
            txtContent = new System.Windows.Forms.TextBox();
            btnStartHosting = new System.Windows.Forms.Button();
            btnStopHosting = new System.Windows.Forms.Button();
            bNotice = new System.Windows.Forms.Button();
            bWarn = new System.Windows.Forms.Button();
            btnTestTraceScope2 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize) gadgetGrid).BeginInit();
            ((System.ComponentModel.ISupportInitialize) widgetGrid).BeginInit();
            SuspendLayout();
            // 
            // gadgetGrid
            // 
            gadgetGrid.Anchor =  System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            gadgetGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gadgetGrid.Location = new System.Drawing.Point(1, 40);
            gadgetGrid.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            gadgetGrid.Name = "gadgetGrid";
            gadgetGrid.Size = new System.Drawing.Size(1267, 187);
            gadgetGrid.TabIndex = 0;
            // 
            // bAddGadget
            // 
            bAddGadget.Anchor =  System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            bAddGadget.Location = new System.Drawing.Point(1185, 231);
            bAddGadget.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bAddGadget.Name = "bAddGadget";
            bAddGadget.Size = new System.Drawing.Size(88, 27);
            bAddGadget.TabIndex = 1;
            bAddGadget.Text = "Add gadget";
            bAddGadget.UseVisualStyleBackColor = true;
            bAddGadget.Click += bAddGadget_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 16F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            label1.Location = new System.Drawing.Point(14, 7);
            label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(94, 26);
            label1.TabIndex = 2;
            label1.Text = "Gadgets";
            // 
            // bAddWidget
            // 
            bAddWidget.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            bAddWidget.Location = new System.Drawing.Point(1184, 660);
            bAddWidget.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bAddWidget.Name = "bAddWidget";
            bAddWidget.Size = new System.Drawing.Size(88, 27);
            bAddWidget.TabIndex = 4;
            bAddWidget.Text = "Add widget";
            bAddWidget.UseVisualStyleBackColor = true;
            bAddWidget.Click += bAddWidget_Click;
            // 
            // widgetGrid
            // 
            widgetGrid.Anchor =  System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            widgetGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            widgetGrid.Location = new System.Drawing.Point(1, 264);
            widgetGrid.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            widgetGrid.Name = "widgetGrid";
            widgetGrid.Size = new System.Drawing.Size(1272, 183);
            widgetGrid.TabIndex = 3;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 16F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            label2.Location = new System.Drawing.Point(-5, 231);
            label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(91, 26);
            label2.TabIndex = 5;
            label2.Text = "Widgets";
            // 
            // bInfo
            // 
            bInfo.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            bInfo.Location = new System.Drawing.Point(4, 660);
            bInfo.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bInfo.Name = "bInfo";
            bInfo.Size = new System.Drawing.Size(80, 27);
            bInfo.TabIndex = 6;
            bInfo.Text = "Info";
            bInfo.UseVisualStyleBackColor = true;
            bInfo.Click += bMinorProblem_Click;
            // 
            // bHorrificException
            // 
            bHorrificException.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            bHorrificException.Location = new System.Drawing.Point(252, 660);
            bHorrificException.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bHorrificException.Name = "bHorrificException";
            bHorrificException.Size = new System.Drawing.Size(182, 27);
            bHorrificException.TabIndex = 7;
            bHorrificException.Text = "Generate Horrific Exception";
            bHorrificException.UseVisualStyleBackColor = true;
            bHorrificException.Click += bHorrificException_Click;
            // 
            // bRemoveGadget
            // 
            bRemoveGadget.Anchor =  System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            bRemoveGadget.Location = new System.Drawing.Point(1064, 231);
            bRemoveGadget.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bRemoveGadget.Name = "bRemoveGadget";
            bRemoveGadget.Size = new System.Drawing.Size(114, 27);
            bRemoveGadget.TabIndex = 8;
            bRemoveGadget.Text = "Remove gadget";
            bRemoveGadget.UseVisualStyleBackColor = true;
            bRemoveGadget.Click += bRemoveGadget_Click;
            // 
            // bRemoveWidget
            // 
            bRemoveWidget.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            bRemoveWidget.Location = new System.Drawing.Point(1063, 660);
            bRemoveWidget.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bRemoveWidget.Name = "bRemoveWidget";
            bRemoveWidget.Size = new System.Drawing.Size(114, 27);
            bRemoveWidget.TabIndex = 9;
            bRemoveWidget.Text = "Remove widget";
            bRemoveWidget.UseVisualStyleBackColor = true;
            bRemoveWidget.Click += bRemoveWidget_Click;
            // 
            // btnTraceScope
            // 
            btnTraceScope.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btnTraceScope.Location = new System.Drawing.Point(441, 660);
            btnTraceScope.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnTraceScope.Name = "btnTraceScope";
            btnTraceScope.Size = new System.Drawing.Size(134, 27);
            btnTraceScope.TabIndex = 10;
            btnTraceScope.Text = "Test Trace Scope 1";
            btnTraceScope.UseVisualStyleBackColor = true;
            btnTraceScope.Click += btnTraceScope_Click;
            // 
            // btn100
            // 
            btn100.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btn100.Location = new System.Drawing.Point(686, 660);
            btn100.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn100.Name = "btn100";
            btn100.Size = new System.Drawing.Size(97, 27);
            btn100.TabIndex = 11;
            btn100.Text = "100 Events";
            btn100.UseVisualStyleBackColor = true;
            btn100.Click += btn100_Click;
            // 
            // btn1000
            // 
            btn1000.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btn1000.Location = new System.Drawing.Point(790, 660);
            btn1000.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn1000.Name = "btn1000";
            btn1000.Size = new System.Drawing.Size(97, 27);
            btn1000.TabIndex = 12;
            btn1000.Text = "1000 Events";
            btn1000.UseVisualStyleBackColor = true;
            btn1000.Click += btn1000_Click;
            // 
            // btn10
            // 
            btn10.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btn10.Location = new System.Drawing.Point(582, 660);
            btn10.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn10.Name = "btn10";
            btn10.Size = new System.Drawing.Size(97, 27);
            btn10.TabIndex = 13;
            btn10.Text = "10 Events";
            btn10.UseVisualStyleBackColor = true;
            btn10.Click += btn10_Click;
            // 
            // chkSystem
            // 
            chkSystem.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            chkSystem.AutoSize = true;
            chkSystem.Location = new System.Drawing.Point(22, 694);
            chkSystem.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            chkSystem.Name = "chkSystem";
            chkSystem.Size = new System.Drawing.Size(64, 19);
            chkSystem.TabIndex = 14;
            chkSystem.Text = "System";
            chkSystem.UseVisualStyleBackColor = true;
            // 
            // chkWidgets
            // 
            chkWidgets.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            chkWidgets.AutoSize = true;
            chkWidgets.Location = new System.Drawing.Point(99, 694);
            chkWidgets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            chkWidgets.Name = "chkWidgets";
            chkWidgets.Size = new System.Drawing.Size(69, 19);
            chkWidgets.TabIndex = 15;
            chkWidgets.Text = "Widgets";
            chkWidgets.UseVisualStyleBackColor = true;
            // 
            // chkGadgets
            // 
            chkGadgets.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            chkGadgets.AutoSize = true;
            chkGadgets.Location = new System.Drawing.Point(175, 694);
            chkGadgets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            chkGadgets.Name = "chkGadgets";
            chkGadgets.Size = new System.Drawing.Size(69, 19);
            chkGadgets.TabIndex = 16;
            chkGadgets.Text = "Gadgets";
            chkGadgets.UseVisualStyleBackColor = true;
            // 
            // txtContent
            // 
            txtContent.Anchor =  System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtContent.Location = new System.Drawing.Point(4, 455);
            txtContent.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            txtContent.Multiline = true;
            txtContent.Name = "txtContent";
            txtContent.Size = new System.Drawing.Size(1269, 198);
            txtContent.TabIndex = 17;
            // 
            // btnStartHosting
            // 
            btnStartHosting.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btnStartHosting.Location = new System.Drawing.Point(584, 689);
            btnStartHosting.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnStartHosting.Name = "btnStartHosting";
            btnStartHosting.Size = new System.Drawing.Size(134, 27);
            btnStartHosting.TabIndex = 18;
            btnStartHosting.Text = "Start Hosting Service";
            btnStartHosting.UseVisualStyleBackColor = true;
            btnStartHosting.Click += btnStartHosting_Click;
            // 
            // btnStopHosting
            // 
            btnStopHosting.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btnStopHosting.Location = new System.Drawing.Point(725, 689);
            btnStopHosting.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnStopHosting.Name = "btnStopHosting";
            btnStopHosting.Size = new System.Drawing.Size(134, 27);
            btnStopHosting.TabIndex = 19;
            btnStopHosting.Text = "Stop Hosting Service";
            btnStopHosting.UseVisualStyleBackColor = true;
            btnStopHosting.Click += StopDiagnostics;
            // 
            // bNotice
            // 
            bNotice.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            bNotice.Location = new System.Drawing.Point(91, 660);
            bNotice.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bNotice.Name = "bNotice";
            bNotice.Size = new System.Drawing.Size(75, 27);
            bNotice.TabIndex = 20;
            bNotice.Text = "Notice";
            bNotice.UseVisualStyleBackColor = true;
            bNotice.Click += bNotice_Click;
            // 
            // bWarn
            // 
            bWarn.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            bWarn.Location = new System.Drawing.Point(173, 660);
            bWarn.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bWarn.Name = "bWarn";
            bWarn.Size = new System.Drawing.Size(75, 27);
            bWarn.TabIndex = 21;
            bWarn.Text = "Warn";
            bWarn.UseVisualStyleBackColor = true;
            bWarn.Click += bWarn_Click;
            // 
            // btnTestTraceScope2
            // 
            btnTestTraceScope2.Anchor =  System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btnTestTraceScope2.Location = new System.Drawing.Point(441, 689);
            btnTestTraceScope2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnTestTraceScope2.Name = "btnTestTraceScope2";
            btnTestTraceScope2.Size = new System.Drawing.Size(134, 27);
            btnTestTraceScope2.TabIndex = 22;
            btnTestTraceScope2.Text = "Test Trace Scope 2";
            btnTestTraceScope2.UseVisualStyleBackColor = true;
            btnTestTraceScope2.Click += btnTestTraceScope2_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1276, 719);
            Controls.Add(btnTestTraceScope2);
            Controls.Add(bWarn);
            Controls.Add(bNotice);
            Controls.Add(btnStopHosting);
            Controls.Add(btnStartHosting);
            Controls.Add(txtContent);
            Controls.Add(chkGadgets);
            Controls.Add(chkWidgets);
            Controls.Add(chkSystem);
            Controls.Add(btn10);
            Controls.Add(btn1000);
            Controls.Add(btn100);
            Controls.Add(btnTraceScope);
            Controls.Add(bRemoveWidget);
            Controls.Add(bRemoveGadget);
            Controls.Add(bHorrificException);
            Controls.Add(bInfo);
            Controls.Add(label2);
            Controls.Add(bAddWidget);
            Controls.Add(widgetGrid);
            Controls.Add(label1);
            Controls.Add(bAddGadget);
            Controls.Add(gadgetGrid);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "Form1";
            SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            Text = "WidgetSample";
            ((System.ComponentModel.ISupportInitialize) gadgetGrid).EndInit();
            ((System.ComponentModel.ISupportInitialize) widgetGrid).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.DataGridView gadgetGrid;
        private System.Windows.Forms.Button bAddGadget;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button bAddWidget;
        private System.Windows.Forms.DataGridView widgetGrid;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button bInfo;
        private System.Windows.Forms.Button bHorrificException;
        private System.Windows.Forms.Button bRemoveGadget;
        private System.Windows.Forms.Button bRemoveWidget;
        private System.Windows.Forms.Button btnTraceScope;
        private System.Windows.Forms.Button btn100;
        private System.Windows.Forms.Button btn1000;
        private System.Windows.Forms.Button btn10;
        private System.Windows.Forms.CheckBox chkSystem;
        private System.Windows.Forms.CheckBox chkWidgets;
        private System.Windows.Forms.CheckBox chkGadgets;
        private System.Windows.Forms.TextBox txtContent;
        private System.Windows.Forms.Button btnStartHosting;
        private System.Windows.Forms.Button btnStopHosting;
        private System.Windows.Forms.Button bNotice;
        private System.Windows.Forms.Button bWarn;
        private System.Windows.Forms.Button btnTestTraceScope2;
    }
}

