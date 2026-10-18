// Views rendered into docs/design/generated/ for the Design document.
// Reopens the WordSense package so each 'expose' statement can reference
// its target part def directly. Per SysML2Tools syntax, 'expose' is only valid
// inside a named 'view' usage (not a 'view def' definition), and it - not
// 'render' - is what scopes the rendered diagram content.
package WordSense {
    view SoftwareStructureView {
        expose WordSense;
    }

    view WordSenseView {
        expose WordSenseSystem;
    }

    view CoreView {
        expose CoreSubsystem;
    }

    view WordDataView {
        expose WordDataSubsystem;
    }

    view BasicView {
        expose BasicSubsystem;
    }

    view OnnxView {
        expose OnnxSubsystem;
    }

    view MiniLMView {
        expose MiniLMSubsystem;
    }

    view BertBaseCasedView {
        expose BertBaseCasedSubsystem;
    }
}
